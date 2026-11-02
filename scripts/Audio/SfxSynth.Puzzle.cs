namespace Wavebreak.Audio;

/// <summary>The puzzle maps' sounds (Game/Puzzle), synthesised like every
/// other effect: a stone click for plates and levers, a wooden thunk for a
/// pushed crate, a low rumble for a door, a soft "nope" for a wrong bell, a
/// rising arpeggio for a solved room, a breathy whoosh for a gust, and the
/// bell itself (one tone, pitched per bell by the player).</summary>
internal static partial class SfxSynth
{
    public static float[] BuildPuzzle(PuzzleSound s)
    {
        float[] b;
        switch (s)
        {
            case PuzzleSound.Click:
                b = New(0.12f);
                Tone(b, 0f, 0.1f, 520f, 380f, Wave.Triangle, 0.8f, 0.002f, 40f);
                Noise(b, new NoiseGen(0xC11Cu), 0f, 0.05f, 2400f, 900f, 300f, 0.35f, 0.001f, 60f);
                break;
            case PuzzleSound.Thunk:
                b = New(0.22f);
                Tone(b, 0f, 0.2f, 150f, 90f, Wave.Sine, 0.9f, 0.003f, 18f);
                Noise(b, new NoiseGen(0x7A4Bu), 0f, 0.16f, 900f, 300f, 60f, 0.5f, 0.002f, 22f);
                break;
            case PuzzleSound.Door:
                b = New(0.9f);
                Noise(b, new NoiseGen(0xD00Bu), 0f, 0.85f, 260f, 120f, 30f, 0.9f, 0.05f, 3.5f);
                Tone(b, 0f, 0.8f, 70f, 55f, Wave.Sine, 0.6f, 0.04f, 4f);
                break;
            case PuzzleSound.Bell:
                b = New(1.1f);
                Tone(b, 0f, 1.1f, 784f, 784f, Wave.Sine, 0.7f, 0.003f, 4.5f);
                Tone(b, 0f, 0.8f, 784f * 2.76f, 784f * 2.76f, Wave.Sine, 0.18f, 0.002f, 8f);
                Tone(b, 0f, 0.5f, 784f * 5.4f, 784f * 5.4f, Wave.Triangle, 0.05f, 0.002f, 12f);
                break;
            case PuzzleSound.Wrong:
                b = New(0.4f);
                Arpeggio(b, new[] { 330f, 262f }, 0.12f, 7f, glide: 0.92f);
                break;
            case PuzzleSound.Solved:
                b = New(0.95f);
                Arpeggio(b, new[] { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f }, 0.07f, 4f);
                break;
            case PuzzleSound.Gust:
                b = New(1.0f);
                Noise(b, new NoiseGen(0x6057u), 0f, 1.0f, 500f, 2200f, 200f, 0.8f, 0.3f, 2.2f);
                break;
            default:   // Reward
                b = New(1.3f);
                Arpeggio(b, new[] { 392f, 523.25f, 659.25f, 783.99f, 1046.5f, 1568f }, 0.09f, 3f);
                break;
        }
        LowPass(b, Smoothing);
        Normalize(b, Peak * 0.8f);
        return b;
    }
}
