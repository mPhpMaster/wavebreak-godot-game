namespace Wavebreak.Audio;

/// <summary>The Tide Totem's call (Game/Interactables.Totems.cs): a soft conch
/// "hoo" that lifts a fifth, a wave of filtered noise rushing in and up, and
/// four glassy shell chimes on top, about 1.3 s. Same synth helpers and peak
/// as every other effect.</summary>
internal static partial class SfxSynth
{
    public static float[] BuildTotem()
    {
        float[] b = New(1.35f);
        // Conch: low triangle rising a fifth, with a quiet octave.
        Tone(b, 0f, 0.95f, 196f, 294f, Wave.Triangle, 0.55f, 0.06f, 2.6f);
        Tone(b, 0f, 0.95f, 392f, 588f, Wave.Sine, 0.18f, 0.06f, 3.2f);
        // The wave: noise whose low-pass opens as it rushes in.
        Noise(b, new NoiseGen(0x71DEu), 0.02f, 0.9f, 300f, 3400f, 120f, 0.75f, 0.25f, 3.5f);
        // Shell chimes, C6 E6 G6 C7, entering on the crest.
        float[] notes = { 1046.5f, 1318.5f, 1568f, 2093f };
        for (int k = 0; k < notes.Length; k++)
        {
            float start = 0.30f + k * 0.075f;
            Tone(b, start, 1.35f - start, notes[k], notes[k], Wave.Sine, 0.32f, 0.004f, 4.5f);
            Tone(b, start, 1.35f - start, notes[k] * 2f, notes[k] * 2f, Wave.Triangle, 0.05f, 0.004f, 7f);
        }
        LowPass(b, Smoothing);
        Normalize(b, Peak * 0.85f);
        return b;
    }
}
