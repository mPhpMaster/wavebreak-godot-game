namespace Wavebreak.Audio;

/// <summary>
/// Weapon voices (Combat/Weapons/WeaponLooks.cs): look-alike weapons used to
/// fire with one sound per shape, so a rail, a fan and a sniper all puffed the
/// same. Each voice is as soft as the old shape sounds (sine and triangle,
/// low-passed noise, the same envelope floors) and as thin: they sit under
/// everything and are rate-limited like the shape sounds.
/// </summary>
internal static partial class SfxSynth
{
    /// <summary>A weapon voice, or null when <paramref name="id"/> is not one.</summary>
    private static float[]? BuildVoice(Sfx id, NoiseGen rng, float d)
    {
        float[] b;
        switch (id)
        {
            case Sfx.FireGlass:
                // A small glass ting: two high sines a fifth apart.
                b = New(0.12f);
                Tone(b, 0f, 0.11f, 1760f * d, 1700f * d, Wave.Sine, 0.8f, 0.004f, 30f);
                Tone(b, 0f, 0.08f, 2637f * d, 2600f * d, Wave.Sine, 0.3f, 0.004f, 40f);
                return b;
            case Sfx.FireZap:
                // A crackle: filtered noise over a fast falling triangle.
                b = New(0.1f);
                Noise(b, rng, 0f, 0.09f, 2400f, 1200f, 600f, 0.8f, 0.004f, 35f);
                Tone(b, 0f, 0.08f, 900f * d, 300f * d, Wave.Triangle, 0.35f, 0.004f, 40f);
                return b;
            case Sfx.FireThunk:
                // A low wooden thunk.
                b = New(0.16f);
                Tone(b, 0f, 0.15f, 180f * d, 70f * d, Wave.Sine, 1f, 0.004f, 20f);
                Noise(b, rng, 0f, 0.05f, 900f, 300f, 60f, 0.4f, 0.004f, 60f);
                return b;
            case Sfx.FireCrack:
                // A sharp crack with a short tail: the sniper and the lance.
                b = New(0.2f);
                Noise(b, rng, 0f, 0.04f, 2500f, 1800f, 800f, 1f, 0.004f, 80f);
                Tone(b, 0.01f, 0.18f, 420f * d, 160f * d, Wave.Sine, 0.5f, 0.004f, 14f);
                return b;
            case Sfx.FireChime:
                // A soft bell: wisps and cold fields.
                b = New(0.3f);
                Tone(b, 0f, 0.28f, 1046.5f * d, 1046.5f * d, Wave.Sine, 0.7f, 0.006f, 10f);
                Tone(b, 0f, 0.2f, 2093f * d, 2093f * d, Wave.Sine, 0.18f, 0.006f, 16f);
                return b;
            case Sfx.FireBuzz:
                // A wing buzz: two triangles beating.
                b = New(0.16f);
                Tone(b, 0f, 0.15f, 220f * d, 240f * d, Wave.Triangle, 0.6f, 0.02f, 12f);
                Tone(b, 0f, 0.15f, 228f * d, 236f * d, Wave.Triangle, 0.5f, 0.02f, 12f);
                return b;
            case Sfx.FirePop:
                // A wet pop: a rising blip.
                b = New(0.09f);
                Tone(b, 0f, 0.08f, 260f * d, 620f * d, Wave.Sine, 1f, 0.004f, 40f);
                return b;
            case Sfx.FireWhoosh:
                // A swing: band noise sweeping up then down.
                b = New(0.18f);
                Noise(b, rng, 0f, 0.17f, 500f * d, 1400f * d, 250f, 1f, 0.05f, 16f);
                return b;
            case Sfx.FireHiss:
                b = New(0.22f);
                Noise(b, rng, 0f, 0.21f, 2200f, 1800f, 900f, 0.8f, 0.03f, 10f);
                return b;
            case Sfx.FireHum:
                // A warm hum: fields, beams, rings.
                b = New(0.2f);
                Tone(b, 0f, 0.19f, 110f * d, 116f * d, Wave.Sine, 0.9f, 0.03f, 12f);
                Tone(b, 0f, 0.19f, 220f * d, 232f * d, Wave.Triangle, 0.25f, 0.03f, 14f);
                return b;
            case Sfx.FireClang:
                // Steel on steel, damped.
                b = New(0.18f);
                Tone(b, 0f, 0.17f, 660f * d, 640f * d, Wave.Triangle, 0.6f, 0.004f, 18f);
                Tone(b, 0f, 0.12f, 1580f * d, 1560f * d, Wave.Sine, 0.25f, 0.004f, 26f);
                Noise(b, rng, 0f, 0.03f, 2400f, 2000f, 900f, 0.3f, 0.004f, 90f);
                return b;
            case Sfx.FireTwang:
                // A plucked string: a crossbow, a sling.
                b = New(0.16f);
                Tone(b, 0f, 0.15f, 330f * d, 300f * d, Wave.Triangle, 0.8f, 0.004f, 20f);
                Tone(b, 0f, 0.1f, 990f * d, 900f * d, Wave.Sine, 0.2f, 0.004f, 30f);
                return b;
        }
        return null;
    }
}
