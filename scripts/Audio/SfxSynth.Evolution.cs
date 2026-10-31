namespace Wavebreak.Audio;

/// <summary>The evolution sting (Audio/AudioDirector.Evolution.cs): a low swell
/// climbing into a bright major chord, with the same synth helpers as every
/// other effect.</summary>
internal static partial class SfxSynth
{
    public static float[] BuildEvolution()
    {
        float[] b = New(1.45f);
        Tone(b, 0f, 0.55f, 196f, 392f, Wave.Triangle, 0.55f, 0.18f, 3f);
        Tone(b, 0.10f, 0.45f, 392f, 783.99f, Wave.Sine, 0.35f, 0.15f, 4f);
        Arpeggio(b, new[] { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f }, 0.07f, 3.2f);
        Tone(b, 0.38f, 1.0f, 523.25f, 523.25f, Wave.Sine, 0.45f, 0.01f, 2.6f);
        Tone(b, 0.38f, 1.0f, 783.99f, 783.99f, Wave.Sine, 0.38f, 0.01f, 2.8f);
        Tone(b, 0.38f, 1.0f, 1046.5f, 1046.5f, Wave.Sine, 0.34f, 0.01f, 3f);
        Tone(b, 0.38f, 0.8f, 2093f, 2093f, Wave.Triangle, 0.07f, 0.01f, 4f);
        LowPass(b, Smoothing);
        Normalize(b, Peak);
        return b;
    }
}
