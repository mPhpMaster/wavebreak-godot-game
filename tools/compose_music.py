#!/usr/bin/env python3
"""
Wavebreak music renderer: composes and renders every music track offline.

Everything here is original: the notes come from the tables and the seeded
melody generator below, the sounds from the small synthesiser below. No
samples, no third-party audio, so the output is ours to license (CC0, see
assets/CREDITS.md).

    python tools/compose_music.py                # render all into assets/music
    python tools/compose_music.py --only meadow  # one track
    python tools/compose_music.py --wav-dir D:/tools/megabonk/tmp/music

Needs numpy + scipy, and ffmpeg (libvorbis) on PATH for the OGG step.

Design rules, because the first audio pass was called annoying:
  * soft timbres only: sine/FM bells, additive plucks, filtered pads; every
    instrument is low-passed and the master bus is low-passed again;
  * diatonic, consonant harmony and a melody that moves mostly by step;
  * mastered quietly (about -20 dBFS RMS) so it sits under the effects;
  * loops are rendered *circularly*: note tails and the reverb past the loop
    point are folded back onto the start, so the file is exactly periodic and
    loops without a seam or a click.
"""
from __future__ import annotations

import argparse
import math
import os
import subprocess
import sys
import wave

import numpy as np
from scipy.signal import butter, fftconvolve, sosfilt

SR = 44100
TAU = 2.0 * math.pi


def mtof(m: float) -> float:
    return 440.0 * 2.0 ** ((m - 69) / 12.0)


# --------------------------------------------------------------- filters

_SOS_CACHE: dict = {}


def sos(kind: str, freq, order: int = 2):
    key = (kind, freq if not isinstance(freq, list) else tuple(freq), order)
    if key not in _SOS_CACHE:
        _SOS_CACHE[key] = butter(order, freq, btype=kind, fs=SR, output="sos")
    return _SOS_CACHE[key]


def lowpass(x, f, order=2):
    return sosfilt(sos("lowpass", min(f, SR * 0.45), order), x)


def highpass(x, f, order=2):
    return sosfilt(sos("highpass", f, order), x)


def bandpass(x, lo, hi, order=2):
    return sosfilt(sos("bandpass", [lo, hi], order), x)


# ----------------------------------------------------------- instruments
# Each returns a mono float array starting at the note onset.

def _t(seconds):
    return np.arange(int(seconds * SR)) / SR


def _env(t, dur, attack=0.01, release=0.3, decay=None, sustain=1.0):
    e = np.minimum(t / attack, 1.0) if attack > 0 else np.ones_like(t)
    e = e * e * (3 - 2 * e)  # smoothstep attack: no click
    if decay:
        e = e * (sustain + (1 - sustain) * np.exp(-t / decay))
    rel = np.clip(1.0 - (t - dur) / release, 0.0, 1.0)
    return e * np.where(t > dur, rel * rel, 1.0)


def pad(f, dur, rng, bright=1.0):
    t = _t(dur + 1.6)
    out = np.zeros_like(t)
    vib = 1.0 + 0.0025 * np.sin(TAU * 4.3 * t + rng.uniform(0, TAU))
    for cents in (-7.0, 0.0, 7.0):
        fv = f * 2 ** (cents / 1200)
        for k in range(1, 9):
            if fv * k > 7000:
                break
            out += (1.0 / k ** 1.9) * np.sin(TAU * fv * k * t * vib + rng.uniform(0, TAU))
    out = lowpass(out, 1400 + 1600 * bright)
    return out * _env(t, dur, attack=0.45, release=1.5) * 0.22


def organ(f, dur, rng):
    t = _t(dur + 0.6)
    trem = 1.0 + 0.08 * np.sin(TAU * 5.5 * t)
    out = np.zeros_like(t)
    for k, a in ((1, 1.0), (2, 0.55), (3, 0.3), (4, 0.22), (6, 0.08)):
        out += a * np.sin(TAU * f * k * t + rng.uniform(0, TAU))
    out = lowpass(out, 2200)
    return out * trem * _env(t, dur, attack=0.08, release=0.5) * 0.18


def bell(f, dur, rng, ratio=3.5, index=1.4, decay=1.4):
    t = _t(max(dur, 0.2) + decay * 2.2)
    mod = index * np.exp(-t / 0.22) * np.sin(TAU * f * ratio * t)
    out = np.sin(TAU * f * t + mod)
    out += 0.18 * np.sin(TAU * f * 2.0 * t) * np.exp(-t / (decay * 0.35))
    out = lowpass(out, 6000)
    e = np.minimum(t / 0.004, 1.0) * np.exp(-t / decay)
    return out * e * 0.5


def musicbox(f, dur, rng):
    return bell(f, dur, rng, ratio=4.0, index=0.9, decay=0.9)


def epiano(f, dur, rng):
    t = _t(dur + 1.2)
    idx = 0.9 * np.exp(-t / 0.35)
    out = np.sin(TAU * f * t + idx * np.sin(TAU * f * t))
    out += 0.06 * np.sin(TAU * f * 14 * t) * np.exp(-t / 0.03)  # tine
    out = lowpass(out, 4500)
    e = _env(t, dur, attack=0.003, release=0.35, decay=1.1, sustain=0.0)
    return out * e * 0.45


def pluck(f, dur, rng, bright=1.0):
    t = _t(1.6)
    out = np.zeros_like(t)
    for k in range(1, 11):
        if f * k > 9000:
            break
        a = 1.0 / k ** 1.4
        out += a * np.exp(-t * (2.2 + 1.6 * k ** 1.15 / bright)) * np.sin(TAU * f * k * t + rng.uniform(0, 0.3))
    out = lowpass(out, 5000)
    return out * np.minimum(t / 0.002, 1.0) * 0.4


def bass(f, dur, rng):
    t = _t(dur + 0.2)
    out = np.sin(TAU * f * t) + 0.32 * np.sin(TAU * 2 * f * t) + 0.1 * np.sin(TAU * 3 * f * t)
    out = lowpass(out, 700)
    return out * _env(t, dur, attack=0.008, release=0.12, decay=0.5, sustain=0.65) * 0.55


def lead(f, dur, rng, bright=1.0):
    t = _t(dur + 0.4)
    vib_amt = 0.004 * np.clip((t - 0.2) / 0.3, 0.0, 1.0)
    phase = TAU * f * np.cumsum(1.0 + vib_amt * np.sin(TAU * 5.2 * t)) / SR
    out = np.sin(phase) + 0.3 * np.sin(2 * phase) + 0.12 * bright * np.sin(3 * phase) + 0.05 * bright * np.sin(4 * phase)
    breath = lowpass(rng.standard_normal(len(t)), 2500) * 0.02
    out = lowpass(out + breath, 2600 + 1200 * bright)
    return out * _env(t, dur, attack=0.05, release=0.25) * 0.3


def brass(f, dur, rng):
    """A rounded 'horn' for the boss: more harmonics, but swelled and filtered."""
    t = _t(dur + 0.3)
    out = np.zeros_like(t)
    for k in range(1, 8):
        out += (1.0 / k ** 1.3) * np.sin(TAU * f * k * t * (1 + 0.002 * np.sin(TAU * 5 * t)))
    cutoff_env = _env(t, dur, attack=0.12, release=0.25)
    out = lowpass(out, 1800)
    return out * cutoff_env * 0.22


def kick(rng, vel=1.0):
    t = _t(0.42)
    fr = 44 + 75 * np.exp(-t / 0.04)
    out = np.sin(TAU * np.cumsum(fr) / SR) * np.exp(-t / 0.17)
    return out * np.minimum(t / 0.002, 1.0) * 0.9 * vel


def snare(rng, vel=1.0):
    t = _t(0.3)
    n = bandpass(rng.standard_normal(len(t)), 900, 4500) * np.exp(-t / 0.08)
    tone = np.sin(TAU * 185 * t) * np.exp(-t / 0.05)
    return (0.55 * n + 0.45 * tone) * np.minimum(t / 0.002, 1.0) * 0.5 * vel


def hat(rng, vel=1.0, open_=False):
    t = _t(0.3 if open_ else 0.08)
    n = bandpass(rng.standard_normal(len(t)), 6500, 12000) * np.exp(-t / (0.09 if open_ else 0.02))
    return n * 0.35 * vel


def shaker(rng, vel=1.0):
    t = _t(0.12)
    n = bandpass(rng.standard_normal(len(t)), 3500, 9000)
    e = np.minimum(t / 0.015, 1.0) * np.exp(-t / 0.035)
    return n * e * 0.3 * vel


def tom(f, rng, vel=1.0):
    t = _t(0.5)
    fr = f * (1 + 0.5 * np.exp(-t / 0.05))
    out = np.sin(TAU * np.cumsum(fr) / SR) * np.exp(-t / 0.22)
    return lowpass(out, 900) * 0.6 * vel


def swell(seconds, rng):
    t = _t(seconds)
    n = bandpass(rng.standard_normal(len(t)), 250, 1400)
    e = np.sin(np.pi * t / seconds) ** 2
    return n * e * 0.18


# --------------------------------------------------------------- harmony

NOTE = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}
QUAL = {
    "": (0, 4, 7), "m": (0, 3, 7), "7": (0, 4, 7, 10), "maj7": (0, 4, 7, 11),
    "m7": (0, 3, 7, 10), "sus2": (0, 2, 7), "sus4": (0, 5, 7), "add9": (0, 4, 7, 14),
    "m9": (0, 3, 7, 10, 14), "maj9": (0, 4, 7, 11, 14), "madd9": (0, 3, 7, 14), "dim": (0, 3, 6),
}


def chord(name: str):
    """'F#m7' -> (root pitch class, intervals)."""
    root = NOTE[name[0]]
    rest = name[1:]
    if rest[:1] == "#":
        root, rest = root + 1, rest[1:]
    elif rest[:1] == "b":
        root, rest = root - 1, rest[1:]
    return root % 12, QUAL[rest]


def voicing(name, lo=52):
    """Chord tones placed in one octave above lo (close voicing)."""
    root, ivs = chord(name)
    notes = []
    for iv in ivs:
        pc = (root + iv) % 12
        m = lo + ((pc - lo) % 12)
        notes.append(m)
    return sorted(set(notes))


def scale_notes(pcs, lo, hi):
    return [m for m in range(lo, hi + 1) if m % 12 in pcs]


MAJOR = (0, 2, 4, 5, 7, 9, 11)
MINOR = (0, 2, 3, 5, 7, 8, 10)
DORIAN = (0, 2, 3, 5, 7, 9, 10)
HARM_MINOR = (0, 2, 3, 5, 7, 8, 11)
LYDIAN = (0, 2, 4, 6, 7, 9, 11)


def pcs(key_pc, mode):
    return tuple((key_pc + s) % 12 for s in mode)


# --------------------------------------------------------------- melody

RHYTHMS_4 = [
    [(0, 1), (1, 1), (2, 2)],
    [(0, 1.5), (1.5, 0.5), (2, 2)],
    [(0, 0.5), (0.5, 0.5), (1, 1), (2, 1), (3, 1)],
    [(0, 2), (2, 1), (3, 1)],
    [(0, 1), (1, 0.5), (1.5, 0.5), (2, 1.5), (3.5, 0.5)],
    [(0, 3), (3, 1)],
]
RHYTHMS_3 = [
    [(0, 2), (2, 1)],
    [(0, 1), (1, 1), (2, 1)],
    [(0, 1.5), (1.5, 0.5), (2, 1)],
    [(0, 3)],
]


def melody(rng, chords, scale, lo, hi, beats_per_bar=4, phrase_bars=4):
    """A singable line: a 2-bar rhythmic motif reused across each phrase,
    chord tones on strong beats, stepwise motion between them, and the
    phrase resolving to a long chord tone. Returns [(beat, dur, midi)]."""
    lib = RHYTHMS_4 if beats_per_bar == 4 else RHYTHMS_3
    notes = scale_notes(scale, lo, hi)
    out = []
    prev = notes[len(notes) // 2]
    motif = None
    for bar, name in enumerate(chords):
        pos_in_phrase = bar % phrase_bars
        if pos_in_phrase == 0:
            motif = [lib[rng.integers(len(lib))], lib[rng.integers(len(lib))]]
        last_of_phrase = pos_in_phrase == phrase_bars - 1
        rhythm = [(0, beats_per_bar - 1), (beats_per_bar - 1, 1)] if last_of_phrase else motif[bar % 2]
        root, ivs = chord(name)
        tones = [m for m in notes if (m - root) % 12 in [i % 12 for i in ivs]]
        for j, (b, d) in enumerate(rhythm):
            if last_of_phrase and j == 1:
                continue  # breathe before the next phrase
            strong = (b % 2 == 0)
            if strong or last_of_phrase:
                cand = sorted(tones, key=lambda m: abs(m - prev) + rng.uniform(0, 2.5))
                m = cand[0]
            else:
                i = notes.index(min(notes, key=lambda m: abs(m - prev)))
                step = rng.choice([-1, 1, 1, -2, 2]) if rng.uniform() < 0.85 else 0
                # lean back toward the middle of the range
                if prev > (lo + hi) / 2 + 4:
                    step = -abs(step)
                elif prev < (lo + hi) / 2 - 4:
                    step = abs(step)
                m = notes[int(np.clip(i + step, 0, len(notes) - 1))]
            out.append((bar * beats_per_bar + b, d, m))
            prev = m
    return out


# ----------------------------------------------------------------- track

class Track:
    def __init__(self, name, bpm, bars, beats_per_bar=4, loop=True, tail=4.0, seed=1):
        self.name = name
        self.spb = 60.0 / bpm
        self.beats_per_bar = beats_per_bar
        self.bars = bars
        self.loop = loop
        self.rng = np.random.default_rng(seed)
        body = bars * beats_per_bar * self.spb
        self.length = int(round(body * SR)) if loop else int(round((body + tail) * SR))
        self.extra = int(tail * SR)
        n = self.length + self.extra
        self.dry = np.zeros((2, n))
        self.wet = np.zeros((2, n))

    def add(self, sig, beat, pan=0.0, gain=1.0, send=0.25, humanise=True):
        if humanise:
            beat += self.rng.uniform(-0.006, 0.006) / self.spb
            gain *= self.rng.uniform(0.9, 1.05)
        i0 = max(0, int(round(beat * self.spb * SR)))
        n = min(len(sig), self.dry.shape[1] - i0)
        if n <= 0:
            return
        a = (pan + 1) * math.pi / 4
        l, r = math.cos(a) * gain, math.sin(a) * gain
        s = sig[:n]
        self.dry[0, i0:i0 + n] += s * l
        self.dry[1, i0:i0 + n] += s * r
        if send:
            self.wet[0, i0:i0 + n] += s * l * send
            self.wet[1, i0:i0 + n] += s * r * send

    def beats(self, b):
        return b * self.spb

    def render(self, reverb_seconds=2.4, target_rms_db=-20.0, master_lp=8500):
        L = self.length
        dry, wet = self.dry, self.wet
        if self.loop:
            # Fold everything past the loop point back onto the start.
            dry = dry[:, :L] + np.pad(dry[:, L:], ((0, 0), (0, L - self.extra)))
            wet = wet[:, :L] + np.pad(wet[:, L:], ((0, 0), (0, L - self.extra)))
        else:
            dry, wet = dry[:, :L], wet[:, :L]

        ir = make_ir(reverb_seconds, self.rng)
        rev = np.zeros_like(wet)
        for c in range(2):
            if self.loop:
                rev[c] = np.fft.irfft(np.fft.rfft(wet[c]) * np.fft.rfft(ir[c], L), L)
            else:
                rev[c] = fftconvolve(wet[c], ir[c])[:L]
        mix = dry + rev

        # Master: gentle high cut, filtered circularly for loops.
        for c in range(2):
            mix[c] = filt_circular(lambda x: lowpass(x, master_lp), mix[c], self.loop)
            mix[c] = filt_circular(lambda x: highpass(x, 30), mix[c], self.loop)

        rms = math.sqrt(float(np.mean(mix ** 2))) + 1e-12
        mix *= 10 ** (target_rms_db / 20) / rms
        mix = np.tanh(mix / 0.9) * 0.9  # only touches rare peaks
        if not self.loop:
            fade = int(0.4 * SR)
            mix[:, -fade:] *= np.linspace(1, 0, fade) ** 2
        return mix


def make_ir(seconds, rng):
    t = _t(seconds)
    ir = np.zeros((2, len(t)))
    pre = int(0.022 * SR)
    for c in range(2):
        n = rng.standard_normal(len(t)) * np.exp(-6.9 * t / seconds)
        n = lowpass(n, 4200)
        n[:pre] = 0
        ir[c] = n / math.sqrt(float(np.sum(n ** 2)))
    return ir * 0.9


def filt_circular(fn, x, loop):
    if not loop:
        return fn(x)
    pad_n = min(len(x), SR * 2)
    y = fn(np.concatenate([x[-pad_n:], x]))
    return y[pad_n:]


# ----------------------------------------------------------- arrangement helpers

def play_pads(tr, chords, lo=52, gain=1.0, inst=pad, bright=1.0, pan_spread=0.35, send=0.45):
    bpb = tr.beats_per_bar
    for bar, name in enumerate(chords):
        notes = voicing(name, lo)
        for i, m in enumerate(notes):
            pan = (i / max(1, len(notes) - 1) - 0.5) * 2 * pan_spread
            sig = inst(mtof(m), tr.beats(bpb) * 0.98, tr.rng, bright) if inst is pad else inst(mtof(m), tr.beats(bpb) * 0.98, tr.rng)
            tr.add(sig, bar * bpb, pan=pan, gain=gain, send=send, humanise=False)


def play_bass(tr, chords, pattern, octave=36, gain=1.0, fifths=False):
    """pattern: [(beat, dur)] per bar; fifths alternates root/fifth."""
    bpb = tr.beats_per_bar
    for bar, name in enumerate(chords):
        root, _ = chord(name)
        m0 = octave + root
        for j, (b, d) in enumerate(pattern):
            m = m0 + (7 if fifths and j % 2 == 1 else 0)
            tr.add(bass(mtof(m), tr.beats(d), tr.rng), bar * bpb + b, gain=gain, send=0.05)


def play_arp(tr, chords, inst, lo, step=0.5, order=(0, 1, 2, 1), gain=1.0, octaves=1, pan=0.0, send=0.3, skip_bars=()):
    bpb = tr.beats_per_bar
    for bar, name in enumerate(chords):
        if bar in skip_bars:
            continue
        notes = voicing(name, lo)
        pool = notes + [n + 12 for n in notes][: max(0, octaves - 1) * len(notes)]
        k = 0
        b = 0.0
        while b < bpb - 1e-6:
            m = pool[order[k % len(order)] % len(pool)]
            p = pan + (0.25 if k % 2 else -0.25) * (1 if pan == 0 else 0)
            tr.add(inst(mtof(m), tr.beats(step), tr.rng), bar * bpb + b, pan=p, gain=gain, send=send)
            k += 1
            b += step


def play_melody(tr, mel, inst, gain=1.0, pan=0.0, send=0.35, octave=0):
    for beat, dur, m in mel:
        tr.add(inst(mtof(m + octave), tr.beats(dur) * 0.95, tr.rng), beat, pan=pan, gain=gain, send=send)


def play_drums(tr, bars, pattern, gain=1.0, skip_bars=()):
    """pattern: dict of name -> list of (beat, velocity) in one bar."""
    bpb = tr.beats_per_bar
    for bar in range(bars):
        if bar in skip_bars:
            continue
        for name, hits in pattern.items():
            for b, v in hits:
                if name == "kick":
                    sig, pan, send = kick(tr.rng, v), 0.0, 0.05
                elif name == "snare":
                    sig, pan, send = snare(tr.rng, v), 0.05, 0.25
                elif name == "hat":
                    sig, pan, send = hat(tr.rng, v), 0.3, 0.1
                elif name == "ohat":
                    sig, pan, send = hat(tr.rng, v, open_=True), 0.3, 0.15
                elif name == "shaker":
                    sig, pan, send = shaker(tr.rng, v), -0.35, 0.15
                elif name == "tomlo":
                    sig, pan, send = tom(90, tr.rng, v), -0.2, 0.2
                elif name == "tomhi":
                    sig, pan, send = tom(140, tr.rng, v), 0.2, 0.2
                else:
                    continue
                tr.add(sig, bar * bpb + b, pan=pan, gain=gain, send=send)


def eighths(v, n=8, offset=0.0):
    return [(i * 0.5 + offset, v * (1.0 if i % 2 == 0 else 0.7)) for i in range(n)]


# ------------------------------------------------------------------ tracks

def t_title():
    prog = ["Dmaj7", "Bm7", "Gmaj7", "A", "Dmaj7", "F#m7", "Gmaj9", "Asus4",
            "Em7", "F#m7", "Gmaj7", "A", "Bm7", "Gmaj7", "Em7", "Asus4"]
    tr = Track("title", 80, len(prog), seed=11)
    play_pads(tr, prog, lo=50, gain=0.9)
    play_bass(tr, prog, [(0, 2.8), (3, 0.9)], octave=38, gain=0.8)
    play_arp(tr, prog, epiano, 62, step=0.5, order=(0, 1, 2, 3, 2, 1, 0, 2), gain=0.45, send=0.4)
    mel = melody(tr.rng, prog, pcs(2, MAJOR), 69, 86)
    play_melody(tr, [n for n in mel if n[0] < 8 * 4], bell, gain=0.5, pan=0.15, send=0.5)
    play_melody(tr, [n for n in mel if n[0] >= 8 * 4], lead, gain=0.55, pan=-0.1, send=0.45, octave=-12)
    play_drums(tr, len(prog), {"shaker": eighths(0.5)}, gain=0.6, skip_bars=range(0, 8))
    return tr, dict(reverb_seconds=3.0, target_rms_db=-21)


def t_meadow():
    prog = ["G", "D", "Em", "C", "G", "D", "C", "D",
            "Em", "C", "G", "D", "C", "D", "G", "Gsus4"] * 2
    tr = Track("meadow", 104, len(prog), seed=21)
    play_pads(tr, prog, lo=55, gain=0.55)
    play_bass(tr, prog, [(0, 1.4), (1.5, 0.4), (2, 1.4), (3.5, 0.4)], octave=31, gain=0.85, fifths=True)
    play_arp(tr, prog, pluck, 60, step=0.5, order=(0, 2, 1, 2, 0, 2, 1, 3), gain=0.55, octaves=2)
    drums = {"kick": [(0, 0.8), (2, 0.65)], "snare": [(1, 0.35), (3, 0.4)], "shaker": eighths(0.55)}
    play_drums(tr, len(prog), drums, gain=0.7)
    mel = melody(tr.rng, prog, (7, 9, 11, 2, 4), 67, 84)  # G major pentatonic
    play_melody(tr, [n for n in mel if 8 * 4 <= n[0] < 24 * 4], lead, gain=0.6, pan=0.1)
    play_melody(tr, [n for n in mel if n[0] >= 24 * 4], bell, gain=0.45, pan=-0.15, send=0.45)
    return tr, dict(reverb_seconds=2.2)


def t_frost():
    prog = ["Dm9", "Bbmaj7", "Fmaj7", "C", "Gm7", "Bbmaj7", "Dm", "Asus4",
            "Dm9", "Bbmaj7", "Fmaj7", "C", "Gm7", "Bbmaj7", "Asus4", "A",
            "Bbmaj7", "C", "Dm9", "Dm9", "Gm7", "C", "Asus4", "A"]
    tr = Track("frost", 84, len(prog), seed=31)
    play_pads(tr, prog, lo=50, gain=0.85, bright=0.6, send=0.6)
    play_bass(tr, prog, [(0, 3.6)], octave=26, gain=0.75)
    play_arp(tr, prog, bell, 74, step=0.5, order=(0, 2, 1, 3, 2, 0, 3, 1), gain=0.28, send=0.6)
    drums = {"kick": [(0, 0.55)], "hat": [(1.5, 0.25), (3.5, 0.3)]}
    play_drums(tr, len(prog), drums, gain=0.6, skip_bars=(0, 1, 2, 3))
    mel = melody(tr.rng, prog, pcs(2, DORIAN), 69, 86)
    play_melody(tr, [n for n in mel if n[0] >= 8 * 4], musicbox, gain=0.55, pan=0.2, send=0.55)
    return tr, dict(reverb_seconds=3.6, target_rms_db=-21, master_lp=7500)


def t_ashlands():
    prog = ["Am", "F", "G", "Em", "Am", "Bb", "G", "Am",
            "F", "G", "Am", "Am", "Dm", "Bb", "Esus4", "E"] + \
           ["Am", "F", "G", "Em", "Dm", "Bb", "Esus4", "E"]
    tr = Track("ashlands", 96, len(prog), seed=41)
    play_pads(tr, prog, lo=45, gain=0.6, inst=organ, send=0.35)
    play_bass(tr, prog, [(i * 0.5, 0.4) for i in range(8)], octave=33, gain=0.6)
    drums = {"kick": [(0, 0.85), (1.5, 0.5), (2, 0.75)], "snare": [(3, 0.45)],
             "tomlo": [(2.5, 0.45)], "tomhi": [(3.5, 0.35)], "hat": eighths(0.3)}
    play_drums(tr, len(prog), drums, gain=0.75)
    mel = melody(tr.rng, prog, (9, 0, 2, 4, 7), 57, 74)  # A minor pentatonic
    play_melody(tr, [n for n in mel if n[0] >= 4 * 4], lead, gain=0.6, pan=-0.1, send=0.3)
    play_arp(tr, prog, pluck, 57, step=1.0, order=(0, 1, 2, 1), gain=0.35, send=0.3, skip_bars=range(0, 8))
    return tr, dict(reverb_seconds=2.0, master_lp=7000)


def t_graveyard():
    prog = ["Em", "Am", "B7", "Em", "C", "Am", "B7", "B7",
            "Em", "G", "Am", "B7", "C", "Am", "B7", "Em"] * 2
    tr = Track("graveyard", 96, len(prog), beats_per_bar=3, seed=51)
    play_pads(tr, prog, lo=52, gain=0.55, inst=organ, send=0.55)
    play_bass(tr, prog, [(0, 0.9)], octave=28, gain=0.8)
    # oom-pah-pah: soft plucked chords on 2 and 3
    for bar, name in enumerate(prog):
        for b in (1, 2):
            for m in voicing(name, 59):
                tr.add(pluck(mtof(m), 0.4, tr.rng), bar * 3 + b, pan=0.2, gain=0.22, send=0.35)
    mel = melody(tr.rng, prog, pcs(4, HARM_MINOR), 71, 88, beats_per_bar=3)
    play_melody(tr, mel, musicbox, gain=0.55, pan=-0.15, send=0.55)
    drums = {"kick": [(0, 0.5)], "shaker": [(1, 0.4), (2, 0.4)]}
    play_drums(tr, len(prog), drums, gain=0.6, skip_bars=range(0, 8))
    return tr, dict(reverb_seconds=3.2, target_rms_db=-21, master_lp=7500)


def t_tidal():
    prog = ["Fmaj7", "G", "Em7", "Am7", "Dm7", "Bbmaj7", "Gm7", "Csus4",
            "Fmaj7", "G", "Em7", "Am7", "Bbmaj7", "C", "Fmaj7", "Fmaj7",
            "Dm7", "Em7", "Fmaj7", "G", "Bbmaj7", "Am7", "Gm7", "Csus4"]
    tr = Track("tidal", 90, len(prog), seed=61)
    play_pads(tr, prog, lo=53, gain=0.6, send=0.55)
    play_bass(tr, prog, [(0, 1.4), (1.5, 1.0), (3, 0.9)], octave=29, gain=0.75)
    play_arp(tr, prog, epiano, 60, step=0.5, order=(0, 1, 2, 3, 4, 3, 2, 1), gain=0.42, octaves=2, send=0.45)
    for bar in range(0, len(prog), 2):
        tr.add(swell(tr.beats(8), tr.rng), bar * 4, pan=-0.3 if bar % 4 else 0.3, gain=0.8, send=0.3, humanise=False)
    drums = {"kick": [(0, 0.6), (2.5, 0.4)], "shaker": eighths(0.45)}
    play_drums(tr, len(prog), drums, gain=0.6, skip_bars=(0, 1, 2, 3))
    mel = melody(tr.rng, prog, pcs(5, LYDIAN), 67, 84)
    play_melody(tr, [n for n in mel if n[0] >= 8 * 4], lead, gain=0.55, pan=0.15, send=0.45)
    return tr, dict(reverb_seconds=3.0, target_rms_db=-21)


def t_boss():
    prog = ["Cm", "Ab", "Bb", "Cm", "Cm", "Ab", "Fm", "G",
            "Ab", "Bb", "Cm", "Cm", "Fm", "Ab", "G", "G"] + \
           ["Cm", "Ab", "Bb", "Cm", "Ab", "Bb", "G", "G"]
    tr = Track("boss", 124, len(prog), seed=71)
    play_pads(tr, prog, lo=48, gain=0.5, bright=0.8)
    play_bass(tr, prog, [(i * 0.5, 0.42) for i in range(8)], octave=36, gain=0.7, fifths=False)
    drums = {"kick": [(0, 0.9), (1, 0.7), (2, 0.85), (3, 0.7)], "snare": [(1, 0.5), (3, 0.55)],
             "hat": eighths(0.35, offset=0.0)}
    play_drums(tr, len(prog), drums, gain=0.75)
    for bar in range(3, len(prog), 4):  # tom fill closing each 4 bars
        for b, nm in ((3.0, "tomhi"), (3.25, "tomhi"), (3.5, "tomlo"), (3.75, "tomlo")):
            sig = tom(140 if nm == "tomhi" else 90, tr.rng, 0.5)
            tr.add(sig, bar * 4 + b, pan=0.2 if nm == "tomhi" else -0.2, gain=0.7, send=0.2)
    mel = melody(tr.rng, prog, pcs(0, MINOR), 60, 77)
    play_melody(tr, mel, brass, gain=0.75, pan=0.0, send=0.3)
    play_arp(tr, prog, pluck, 67, step=0.25, order=(0, 1, 2, 1), gain=0.22, send=0.2, skip_bars=range(0, 8))
    return tr, dict(reverb_seconds=1.8, target_rms_db=-19, master_lp=7500)


def t_swarm():
    prog = ["Dm", "Bb", "F", "C", "Dm", "Bb", "Gm", "A",
            "Bb", "C", "Dm", "Dm", "Gm", "Bb", "A", "A"] * 2
    tr = Track("swarm", 136, len(prog), seed=81)
    play_pads(tr, prog, lo=50, gain=0.5)
    play_bass(tr, prog, [(i * 0.5, 0.4) for i in range(8)], octave=38, gain=0.65, fifths=True)
    play_arp(tr, prog, pluck, 62, step=0.25, order=(0, 1, 2, 3, 2, 1), gain=0.35, octaves=2, send=0.2)
    drums = {"kick": [(0, 0.9), (1, 0.75), (2, 0.85), (3, 0.75)], "snare": [(1, 0.55), (3, 0.6)],
             "hat": [(i * 0.25, 0.3 if i % 2 else 0.4) for i in range(16)], "ohat": [(3.5, 0.3)]}
    play_drums(tr, len(prog), drums, gain=0.72)
    mel = melody(tr.rng, prog, pcs(2, MINOR), 64, 81)
    play_melody(tr, [n for n in mel if n[0] < 16 * 4], lead, gain=0.6, pan=0.1, send=0.3)
    play_melody(tr, [n for n in mel if n[0] >= 16 * 4], brass, gain=0.7, pan=-0.05, send=0.3)
    return tr, dict(reverb_seconds=1.8, target_rms_db=-19, master_lp=8000)


def t_victory():
    tr = Track("victory", 120, 2, loop=False, tail=3.5, seed=91)
    for i, m in enumerate([60, 64, 67, 72, 76, 79]):
        tr.add(bell(mtof(m), 0.3, tr.rng), i * 0.25, pan=-0.3 + 0.12 * i, gain=0.6, send=0.5)
    for m in voicing("Cmaj9", 55):
        tr.add(pad(mtof(m), tr.beats(5), tr.rng, 1.0), 1.5, gain=0.9, send=0.5, humanise=False)
    tr.add(bass(mtof(36), tr.beats(5), tr.rng), 1.5, gain=0.8, send=0.05)
    tr.add(lead(mtof(84), tr.beats(4), tr.rng), 2.0, gain=0.5, send=0.5)
    tr.add(bell(mtof(88), 0.5, tr.rng), 2.0, gain=0.35, send=0.6)
    return tr, dict(reverb_seconds=2.8, target_rms_db=-19)


def t_defeat():
    tr = Track("defeat", 72, 2, loop=False, tail=3.5, seed=101)
    for i, m in enumerate([76, 74, 72, 69]):
        tr.add(lead(mtof(m), tr.beats(0.9), tr.rng, bright=0.6), i * 1.0, gain=0.6, send=0.5)
    for m in voicing("Am", 52) + [59]:
        tr.add(pad(mtof(m), tr.beats(5), tr.rng, 0.5), 3.0, gain=0.8, send=0.6, humanise=False)
    tr.add(bass(mtof(33), tr.beats(4), tr.rng), 3.0, gain=0.7, send=0.05)
    return tr, dict(reverb_seconds=3.2, target_rms_db=-21, master_lp=6000)


# ------------------------------------------------------------- boombox set
# The second batch: one "remix" per map (unlocked by that map's quests and
# picked in the Boombox), a cosy menu loop, a final-boss theme distinct from the
# phase boss, and two short stingers. Same rules as above: soft timbres,
# diatonic, quiet master, circular loops. Rendered at a lower OGG quality ("q")
# since they are mostly soft pads and bells and the files should stay small.

def swing(step, i, amt=0.08):
    """Late off-beats for a lazy shuffle feel."""
    return amt if i % 2 else 0.0


def t_lounge():
    prog = ["Fmaj7", "Em7", "Dm7", "Cmaj7", "Bbmaj7", "Am7", "Gm7", "Csus4",
            "Fmaj7", "Am7", "Bbmaj7", "C", "Dm7", "Gm7", "Bbmaj7", "Csus4"]
    tr = Track("lounge", 84, len(prog), seed=111)
    play_pads(tr, prog, lo=53, gain=0.45, bright=0.5, send=0.5)
    # Lazy two-handed e-piano: chord on 1 and the "and" of 2.
    for bar, name in enumerate(prog):
        for b, g in ((0, 0.5), (1.6, 0.32), (3.0, 0.25)):
            for m in voicing(name, 60):
                tr.add(epiano(mtof(m), tr.beats(0.9), tr.rng), bar * 4 + b, pan=-0.15, gain=g * 0.55, send=0.4)
    play_bass(tr, prog, [(0, 1.4), (2.5, 1.2)], octave=29, gain=0.7)
    drums = {"kick": [(0, 0.45), (2.5, 0.35)], "shaker": [(i * 0.5 + swing(0.5, i), 0.35 if i % 2 else 0.25) for i in range(8)],
             "snare": [(3, 0.18)]}
    play_drums(tr, len(prog), drums, gain=0.55)
    mel = melody(tr.rng, prog, pcs(5, MAJOR), 69, 84)
    play_melody(tr, mel, bell, gain=0.38, pan=0.2, send=0.55)
    return tr, dict(reverb_seconds=2.8, target_rms_db=-21, master_lp=7000, q=2)


def t_meadow_dusk():
    prog = ["C", "G", "Am", "F", "C", "G", "F", "G",
            "Am", "F", "C", "G", "F", "G", "C", "Csus4"] + \
           ["F", "G", "Em", "Am", "F", "G", "Csus4", "C"]
    tr = Track("meadow_dusk", 112, len(prog), seed=121)
    play_pads(tr, prog, lo=55, gain=0.45, bright=0.8)
    play_bass(tr, prog, [(0, 0.45), (1, 0.45), (2, 0.45), (2.5, 0.4), (3, 0.45)], octave=36, gain=0.7, fifths=True)
    play_arp(tr, prog, musicbox, 72, step=0.5, order=(0, 1, 2, 1, 0, 2, 1, 2), gain=0.32, send=0.4)
    drums = {"kick": [(0, 0.75), (2, 0.6)], "snare": [(1, 0.32), (3, 0.38)], "hat": eighths(0.28), "shaker": [(1.75, 0.3), (3.75, 0.3)]}
    play_drums(tr, len(prog), drums, gain=0.65, skip_bars=(0, 1))
    mel = melody(tr.rng, prog, (0, 2, 4, 7, 9), 67, 84)  # C major pentatonic
    play_melody(tr, [n for n in mel if n[0] >= 4 * 4], pluck, gain=0.62, pan=0.1, send=0.3)
    play_melody(tr, [n for n in mel if n[0] >= 16 * 4], lead, gain=0.3, pan=-0.2, send=0.4, octave=-12)
    return tr, dict(reverb_seconds=2.0, q=2)


def t_frost_aurora():
    prog = ["Emaj7", "F#m7", "G#m7", "Amaj7", "Emaj7", "C#m7", "Amaj7", "Bsus4",
            "Amaj7", "B", "G#m7", "C#m7", "F#m7", "Amaj7", "Bsus4", "B"]
    tr = Track("frost_aurora", 92, len(prog) * 1, seed=131)
    play_pads(tr, prog, lo=52, gain=0.65, bright=0.5, send=0.65)
    play_bass(tr, prog, [(0, 1.8), (2.5, 1.2)], octave=28, gain=0.65)
    play_arp(tr, prog, bell, 76, step=0.25, order=(0, 1, 2, 3, 2, 1), gain=0.16, send=0.65)
    drums = {"kick": [(0, 0.5), (2.5, 0.35)], "hat": [(1, 0.18), (3, 0.22)], "shaker": eighths(0.25)}
    play_drums(tr, len(prog), drums, gain=0.55, skip_bars=(0, 1, 2, 3))
    mel = melody(tr.rng, prog, pcs(4, LYDIAN), 68, 85)
    play_melody(tr, [n for n in mel if n[0] >= 4 * 4], musicbox, gain=0.5, pan=-0.2, send=0.6)
    return tr, dict(reverb_seconds=3.6, target_rms_db=-21, master_lp=7500, q=2)


def t_ashlands_forge():
    prog = ["Dm7", "G", "Dm7", "G", "Am7", "C", "Dm7", "Esus4",
            "F", "G", "Am7", "Dm7", "Bbmaj7", "C", "Dm7", "Dm7"] + \
           ["Dm7", "G", "Am7", "C", "Bbmaj7", "C", "Esus4", "A"]
    tr = Track("ashlands_forge", 108, len(prog), seed=141)
    # Organ stabs on the off-beats: bouncy, not heavy.
    for bar, name in enumerate(prog):
        for b in (0.5, 1.5, 2.5, 3.5):
            for m in voicing(name, 57):
                tr.add(organ(mtof(m), tr.beats(0.3), tr.rng), bar * 4 + b, pan=0.25, gain=0.22, send=0.25)
    play_pads(tr, prog, lo=50, gain=0.35, bright=0.6)
    play_bass(tr, prog, [(0, 0.8), (1, 0.4), (1.5, 0.4), (2, 0.8), (3, 0.4), (3.5, 0.4)], octave=38, gain=0.7)
    drums = {"kick": [(0, 0.85), (2, 0.75), (2.75, 0.4)], "snare": [(1, 0.45), (3, 0.5)],
             "hat": eighths(0.28), "tomhi": [(3.5, 0.3)]}
    play_drums(tr, len(prog), drums, gain=0.7)
    mel = melody(tr.rng, prog, pcs(2, DORIAN), 62, 79)
    play_melody(tr, [n for n in mel if n[0] >= 8 * 4], lead, gain=0.55, pan=-0.1, send=0.3)
    play_melody(tr, [n for n in mel if n[0] < 8 * 4], pluck, gain=0.55, pan=-0.1, send=0.3, octave=12)
    return tr, dict(reverb_seconds=1.8, master_lp=7500, q=2)


def t_graveyard_waltz():
    prog = ["Gm", "D7", "Gm", "Eb", "Cm", "Gm", "D7", "D7",
            "Bb", "F", "Gm", "Eb", "Cm", "D7", "Gm", "Gm"] * 2
    tr = Track("graveyard_waltz", 138, len(prog), beats_per_bar=3, seed=151)
    play_pads(tr, prog, lo=55, gain=0.35, bright=0.4, send=0.6)
    play_bass(tr, prog, [(0, 0.9)], octave=31, gain=0.75)
    for bar, name in enumerate(prog):
        for b in (1, 2):
            for m in voicing(name, 62):
                tr.add(pluck(mtof(m), 0.3, tr.rng, bright=0.7), bar * 3 + b, pan=-0.2, gain=0.17, send=0.35)
    mel = melody(tr.rng, prog, pcs(7, HARM_MINOR), 67, 84, beats_per_bar=3)
    play_melody(tr, mel, musicbox, gain=0.5, pan=0.15, send=0.55)
    play_melody(tr, [n for n in mel if n[0] >= 16 * 3], bell, gain=0.18, pan=-0.3, send=0.6, octave=12)
    drums = {"kick": [(0, 0.45)], "hat": [(1, 0.18), (2, 0.18)]}
    play_drums(tr, len(prog), drums, gain=0.6, skip_bars=range(0, 4))
    return tr, dict(reverb_seconds=3.0, target_rms_db=-21, master_lp=7500, q=2)


def t_tidal_lagoon():
    prog = ["Bbmaj7", "Gm7", "Cm7", "F", "Bbmaj7", "Dm7", "Ebmaj7", "Fsus4",
            "Ebmaj7", "F", "Dm7", "Gm7", "Cm7", "F", "Bbmaj7", "Bbmaj7"]
    tr = Track("tidal_lagoon", 100, len(prog), seed=161)
    play_pads(tr, prog, lo=53, gain=0.45, send=0.5)
    # Bossa-ish: bass on 1 and the "and" of 2, chords on the syncopations.
    play_bass(tr, prog, [(0, 1.3), (1.5, 0.4), (2, 1.3), (3.5, 0.4)], octave=34, gain=0.7, fifths=True)
    for bar, name in enumerate(prog):
        for b in (0.5, 1.5, 2.5, 3.0):
            for m in voicing(name, 60):
                tr.add(epiano(mtof(m), tr.beats(0.4), tr.rng), bar * 4 + b, pan=0.2, gain=0.14, send=0.4)
    for bar in range(0, len(prog), 4):
        tr.add(swell(tr.beats(8), tr.rng), bar * 4, pan=0.3 if bar % 8 else -0.3, gain=0.6, send=0.3, humanise=False)
    drums = {"kick": [(0, 0.5), (2.5, 0.4)], "shaker": eighths(0.35), "tomhi": [(1.5, 0.2)], "tomlo": [(3.5, 0.22)]}
    play_drums(tr, len(prog), drums, gain=0.6, skip_bars=(0, 1))
    mel = melody(tr.rng, prog, pcs(10, MAJOR), 65, 82)
    play_melody(tr, [n for n in mel if n[0] >= 4 * 4], lead, gain=0.5, pan=-0.15, send=0.45)
    return tr, dict(reverb_seconds=2.6, target_rms_db=-21, q=2)


def t_boss_final():
    prog = ["Em", "C", "D", "Em", "Em", "C", "Am", "B7",
            "C", "D", "Em", "Em", "Am", "C", "B7", "B7"] + \
           ["G", "D", "Em", "C", "Am", "C", "B7", "B7"]
    tr = Track("boss_final", 132, len(prog), seed=171)
    play_pads(tr, prog, lo=52, gain=0.45, bright=0.9)
    play_bass(tr, prog, [(i * 0.5, 0.42) for i in range(8)], octave=40, gain=0.65, fifths=True)
    drums = {"kick": [(0, 0.9), (1, 0.7), (2, 0.85), (2.5, 0.5), (3, 0.7)], "snare": [(1, 0.5), (3, 0.55)],
             "hat": [(i * 0.25, 0.3 if i % 2 else 0.38) for i in range(16)]}
    play_drums(tr, len(prog), drums, gain=0.72)
    for bar in range(7, len(prog), 8):
        for k, b in enumerate((2.0, 2.5, 3.0, 3.25, 3.5, 3.75)):
            sig = tom(140 if k < 3 else 90, tr.rng, 0.55)
            tr.add(sig, bar * 4 + b, pan=0.2 if k < 3 else -0.2, gain=0.7, send=0.2)
    mel = melody(tr.rng, prog, pcs(4, HARM_MINOR), 64, 81)
    play_melody(tr, mel, brass, gain=0.7, pan=0.0, send=0.3)
    play_arp(tr, prog, bell, 76, step=0.5, order=(0, 2, 1, 2), gain=0.16, send=0.4, skip_bars=range(0, 8))
    return tr, dict(reverb_seconds=1.8, target_rms_db=-19, master_lp=7500, q=2)


def t_shop():
    tr = Track("shop", 132, 2, loop=False, tail=2.0, seed=181)
    # "Ding-ding!" up a G major arpeggio, a little turn, and a soft chord.
    for i, (b, m) in enumerate(((0, 67), (0.5, 71), (1, 74), (1.5, 79), (2.25, 78), (2.5, 79), (3, 83))):
        tr.add(musicbox(mtof(m), 0.3, tr.rng), b, pan=-0.25 + 0.08 * i, gain=0.6, send=0.45)
    for m in voicing("Gmaj7", 55):
        tr.add(epiano(mtof(m), tr.beats(3), tr.rng), 3, gain=0.35, send=0.4)
    tr.add(bass(mtof(43), tr.beats(2.5), tr.rng), 3, gain=0.6, send=0.05)
    tr.add(shaker(tr.rng, 0.5), 2.5, pan=0.3, gain=0.6)
    tr.add(shaker(tr.rng, 0.6), 3, pan=0.3, gain=0.6)
    return tr, dict(reverb_seconds=2.0, target_rms_db=-20, q=2)


def t_fanfare():
    tr = Track("fanfare", 112, 3, loop=False, tail=3.5, seed=191)
    # A full-run win: brass call, a bell flourish, and a big warm F major.
    for b, d, m in ((0, 0.5, 65), (0.5, 0.5, 65), (1, 0.5, 65), (1.5, 1.5, 72), (3, 0.5, 70), (3.5, 0.5, 72), (4, 3.5, 77)):
        tr.add(brass(mtof(m), tr.beats(d), tr.rng), b, gain=0.8, send=0.35)
        tr.add(brass(mtof(m - 12), tr.beats(d), tr.rng), b, pan=-0.2, gain=0.35, send=0.35)
    for i, m in enumerate([77, 81, 84, 89, 93]):
        tr.add(bell(mtof(m), 0.3, tr.rng), 4 + i * 0.25, pan=-0.3 + 0.15 * i, gain=0.4, send=0.5)
    for m in voicing("Fadd9", 53):
        tr.add(pad(mtof(m), tr.beats(6), tr.rng, 1.0), 4, gain=0.85, send=0.5, humanise=False)
    tr.add(bass(mtof(41), tr.beats(5), tr.rng), 4, gain=0.8, send=0.05)
    for b, v in ((0, 0.6), (1, 0.5), (1.5, 0.6), (3, 0.5), (3.5, 0.6)):
        tr.add(tom(110, tr.rng, v), b, gain=0.5, send=0.2)
    tr.add(kick(tr.rng, 0.8), 4, gain=0.8, send=0.1)
    return tr, dict(reverb_seconds=2.8, target_rms_db=-19, q=2)


def t_harbour():
    """Stormwatch Harbour: D dorian, lamplit and a little stormy."""
    prog = ["Dm7", "C", "Dm7", "Am7", "Bbmaj7", "C", "Dm7", "Asus4",
            "F", "C", "Dm7", "Am7", "Bbmaj7", "Gm7", "Asus4", "Dm7",
            "Gm7", "C", "F", "Dm7", "Bbmaj7", "Am7", "Gm7", "Asus4"]
    tr = Track("harbour", 96, len(prog), seed=171)
    play_pads(tr, prog, lo=50, gain=0.55, bright=0.6, send=0.55)
    play_bass(tr, prog, [(0, 1.3), (1.5, 0.45), (2, 1.3), (3.5, 0.4)], octave=33, gain=0.75)
    # Concertina-ish organ chords on the off-beats, like a band on the quay.
    for bar, name in enumerate(prog):
        for b in (1, 3):
            for m in voicing(name, 60):
                tr.add(organ(mtof(m), tr.beats(0.7), tr.rng), bar * 4 + b, pan=-0.2, gain=0.12, send=0.35)
    play_arp(tr, prog, pluck, 62, step=0.5, order=(0, 2, 1, 2, 0, 2, 1, 3), gain=0.3, pan=0.25, send=0.35, skip_bars=(0, 1, 2, 3))
    for bar in range(0, len(prog), 4):
        tr.add(swell(tr.beats(8), tr.rng), bar * 4, pan=0.35 if bar % 8 else -0.35, gain=0.7, send=0.3, humanise=False)
    drums = {"kick": [(0, 0.6), (2, 0.45)], "snare": [(3, 0.22)], "shaker": eighths(0.32), "tomlo": [(3.5, 0.2)]}
    play_drums(tr, len(prog), drums, gain=0.6, skip_bars=(0, 1))
    mel = melody(tr.rng, prog, pcs(2, DORIAN), 65, 84)
    play_melody(tr, [n for n in mel if 4 * 4 <= n[0] < 16 * 4], bell, gain=0.42, pan=0.15, send=0.5)
    play_melody(tr, [n for n in mel if n[0] >= 16 * 4], lead, gain=0.5, pan=-0.1, send=0.45)
    return tr, dict(reverb_seconds=2.8, target_rms_db=-21, master_lp=8000, q=2)


def t_harbour_shanty():
    """Stormwatch remix: a quick three-beat shanty in D minor."""
    prog = ["Dm", "Dm", "C", "C", "Dm", "Dm", "Am", "Am",
            "F", "F", "C", "C", "Dm", "Bbmaj7", "Asus4", "Dm"] * 2
    tr = Track("harbour_shanty", 150, len(prog), beats_per_bar=3, seed=181)
    play_pads(tr, prog, lo=52, gain=0.32, bright=0.6, send=0.5)
    play_bass(tr, prog, [(0, 0.9)], octave=33, gain=0.8, fifths=True)
    for bar, name in enumerate(prog):
        for b in (1, 2):
            for m in voicing(name, 60):
                tr.add(organ(mtof(m), tr.beats(0.45), tr.rng), bar * 3 + b, pan=0.2, gain=0.11, send=0.3)
    mel = melody(tr.rng, prog, pcs(2, MINOR), 64, 81, beats_per_bar=3)
    play_melody(tr, mel, pluck, gain=0.55, pan=-0.15, send=0.35)
    play_melody(tr, [n for n in mel if n[0] >= 16 * 3], lead, gain=0.3, pan=0.25, send=0.45, octave=12)
    drums = {"kick": [(0, 0.6)], "tomhi": [(2, 0.22)], "shaker": [(1, 0.3), (2, 0.25), (2.5, 0.2)]}
    play_drums(tr, len(prog), drums, gain=0.6, skip_bars=(0, 1))
    return tr, dict(reverb_seconds=2.2, target_rms_db=-21, q=2)


TRACKS = {
    "title": t_title, "meadow": t_meadow, "frost": t_frost, "ashlands": t_ashlands,
    "graveyard": t_graveyard, "tidal": t_tidal, "boss": t_boss, "swarm": t_swarm,
    "victory": t_victory, "defeat": t_defeat,
    # boombox set
    "lounge": t_lounge, "meadow_dusk": t_meadow_dusk, "frost_aurora": t_frost_aurora,
    "ashlands_forge": t_ashlands_forge, "graveyard_waltz": t_graveyard_waltz,
    "tidal_lagoon": t_tidal_lagoon, "boss_final": t_boss_final, "shop": t_shop, "fanfare": t_fanfare,
    # Stormwatch Harbour
    "harbour": t_harbour, "harbour_shanty": t_harbour_shanty,
}


# ------------------------------------------------------------------ output

def write_wav(path, mix):
    data = np.clip(mix.T, -1, 1)
    pcm = (data * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(here, "..", "assets", "music"))
    ap.add_argument("--wav-dir", default=os.environ.get("WAVEBREAK_WAV_DIR", "D:/tools/megabonk/tmp/music"))
    ap.add_argument("--only", nargs="*")
    ap.add_argument("--quality", default="3")
    args = ap.parse_args()

    os.makedirs(args.out, exist_ok=True)
    os.makedirs(args.wav_dir, exist_ok=True)
    for name, fn in TRACKS.items():
        if args.only and name not in args.only:
            continue
        tr, opts = fn()
        quality = str(opts.pop("q", args.quality))
        mix = tr.render(**opts)
        wav = os.path.join(args.wav_dir, f"{name}.wav")
        write_wav(wav, mix)
        ogg = os.path.join(args.out, f"{name}.ogg")
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-c:a", "libvorbis",
                        "-q:a", quality, ogg], check=True)
        peak = float(np.max(np.abs(mix)))
        rms = 20 * math.log10(math.sqrt(float(np.mean(mix ** 2))) + 1e-12)
        seam = float(np.max(np.abs(mix[:, 0] - mix[:, -1])))
        print(f"{name:10s} {mix.shape[1] / SR:6.2f}s peak={peak:.3f} rms={rms:6.1f}dB "
              f"seam={seam:.4f} loop={tr.loop} -> {os.path.getsize(ogg) / 1024:.0f} KB")


if __name__ == "__main__":
    sys.exit(main())
