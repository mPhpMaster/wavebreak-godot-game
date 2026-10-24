namespace Wavebreak.Audio;

/// <summary>The swap meet's little sounds (UI/TradeUi), built with the same
/// synth helpers as every other effect: a wooden "tok" when an offer changes,
/// a rising two-note "ding-ding" for READY / CONFIRM, a happy arpeggio when a
/// swap completes, and a soft falling pair when one is cancelled.</summary>
internal static partial class SfxSynth
{
    public enum TradeSound { Offer, Accept, Complete, Cancel, Invite }

    public static float[] BuildTrade(TradeSound s)
    {
        float[] b;
        switch (s)
        {
            case TradeSound.Offer:
                b = New(0.11f);
                Tone(b, 0f, 0.10f, 880f, 700f, Wave.Triangle, 0.8f, 0.003f, 38f);
                Tone(b, 0f, 0.06f, 1760f, 1500f, Wave.Sine, 0.25f, 0.002f, 60f);
                break;
            case TradeSound.Accept:
                b = New(0.34f);
                Tone(b, 0f, 0.16f, 1046.5f, 1046.5f, Wave.Sine, 0.7f, 0.004f, 14f);
                Tone(b, 0.10f, 0.24f, 1568f, 1568f, Wave.Sine, 0.7f, 0.004f, 10f);
                Tone(b, 0.10f, 0.24f, 3136f, 3136f, Wave.Triangle, 0.08f, 0.004f, 14f);
                break;
            case TradeSound.Complete:
                b = New(0.85f);
                Arpeggio(b, new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.085f, 4.2f);
                break;
            case TradeSound.Invite:
                b = New(0.4f);
                Tone(b, 0f, 0.18f, 1318.5f, 1318.5f, Wave.Sine, 0.6f, 0.004f, 12f);
                Tone(b, 0.12f, 0.26f, 1046.5f, 1046.5f, Wave.Sine, 0.6f, 0.004f, 10f);
                break;
            default:
                b = New(0.42f);
                Arpeggio(b, new[] { 659.25f, 493.88f }, 0.12f, 6f, glide: 0.94f);
                break;
        }
        LowPass(b, Smoothing);
        Normalize(b, Peak * 0.8f);
        return b;
    }
}
