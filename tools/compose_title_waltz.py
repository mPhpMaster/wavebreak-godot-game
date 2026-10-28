"""Render the original 2026 title rewrite; other music files stay untouched.

Uses the project's original sample-free synthesizer. Run after compose_music.py
when rebuilding every soundtrack file. No notes or recordings are imported.
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
import subprocess

import numpy as np

import compose_music as synth


def compose() -> synth.Track:
    """A quiet 32-bar D-minor music-box waltz, 96 BPM, exactly one minute."""
    chords = [
        "Dm", "Gm", "Bb", "A7", "Dm", "F", "Gm", "A7",
        "Bb", "F", "Gm", "Dm", "Bb", "Gm", "A7", "A7",
    ] * 2
    phrases = [
        [74, 77, 81, 79, 77, 76],
        [74, 70, 67, 70, 74, 77],
        [77, 74, 70, 72, 74, 77],
        [76, 73, 69, 71, 73, 76],
        [77, 76, 74, 69, 74, 77],
        [76, 77, 81, 79, 77, 72],
        [74, 77, 79, 77, 74, 70],
        [73, 76, 79, 76, 73, 69],
        [77, 82, 81, 77, 74, 70],
        [72, 77, 81, 79, 77, 76],
        [74, 79, 82, 81, 79, 77],
        [77, 81, 77, 76, 74, 69],
        [74, 77, 82, 81, 77, 74],
        [74, 70, 67, 69, 70, 74],
        [76, 73, 69, 73, 76, 79],
        [76, 73, 71, 69, 73, 76],
    ]
    track = synth.Track("title", 96, 32, beats_per_bar=3, seed=20261028)
    synth.play_pads(track, chords, lo=50, gain=0.27, bright=0.35, send=0.5)
    synth.play_bass(track, chords, [(0, 1.4)], octave=38, gain=0.52)
    for bar, chord in enumerate(chords):
        for beat in (1, 2):
            for pitch in synth.voicing(chord, 60):
                note = synth.pluck(synth.mtof(pitch), 0.38, track.rng, bright=0.5)
                track.add(note, bar * 3 + beat, gain=0.1, pan=-0.2, send=0.4)
        phrase = phrases[bar % 16]
        for index, pitch in enumerate(phrase):
            if bar % 8 == 7 and index >= 4:
                continue
            gain = 0.46 if index in (0, 2, 4) else 0.34
            note = synth.musicbox(synth.mtof(pitch), 0.42, track.rng)
            track.add(note, bar * 3 + index * 0.5, gain=gain, pan=0.1, send=0.55)
            if bar >= 16 and index == 0:
                harmony = synth.bell(synth.mtof(pitch - 12), 1.2, track.rng)
                track.add(harmony, bar * 3, gain=0.13, pan=-0.25, send=0.6)
    return track


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    args = parser.parse_args()
    track = compose()
    mix = track.render(reverb_seconds=2.8, target_rms_db=-22, master_lp=7000)
    if not np.isfinite(mix).all():
        raise ValueError("Non-finite samples in music render")
    peak = float(np.max(np.abs(mix)))
    seam = float(np.max(np.abs(mix[:, 0] - mix[:, -1])))
    if peak >= 0.95 or seam > 0.01:
        raise ValueError(f"Unsafe peak or loop seam: {peak}, {seam}")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    wav = args.output.with_suffix(".render.wav")
    synth.write_wav(str(wav), mix)
    subprocess.run([
        "ffmpeg", "-y", "-loglevel", "error", "-i", str(wav),
        "-c:a", "libvorbis", "-q:a", "3", str(args.output),
    ], check=True)
    evidence = {
        "source": "Original project synthesis; no third-party samples or melody",
        "bpm": 96, "meter": "3/4", "key": "D harmonic minor",
        "bars": 32, "seconds": mix.shape[1] / synth.SR,
        "peak": peak, "rms_dbfs": 20 * math.log10(float(np.sqrt(np.mean(mix ** 2)))),
        "loop_seam_delta": seam, "ogg_bytes": args.output.stat().st_size,
        "human_listening_verified": False,
    }
    args.evidence.parent.mkdir(parents=True, exist_ok=True)
    args.evidence.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    wav.unlink()
    print(json.dumps(evidence))


if __name__ == "__main__":
    main()
