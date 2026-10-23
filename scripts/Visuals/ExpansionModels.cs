using Godot;
using Wavebreak.Data;

namespace Wavebreak.Visuals;

/// <summary>
/// Models for the enemy expansion (<c>Data/Catalog.Enemies2.cs</c>), fed through
/// the same public hooks as <see cref="BehaviourModels"/>. Flyers wear the
/// Quaternius Flying set (it hovers, so it reads as hovering); enemies whose
/// mechanic needs a specific silhouette — a shield on the front, barnacle
/// crusts, an eel — get an authored procedural body instead, because the shape
/// is the tell. Any imported file that is missing (not yet imported by Godot)
/// falls back to the procedural model classified from the numbers.
/// </summary>
public static class ExpansionModels
{
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered) return;
        _registered = true;

        EnemyVisuals.MapImported("squall_gull", "quaternius/flying/Pigeon.gltf");
        EnemyVisuals.MapImported("lantern_jelly", "quaternius/flying/Glub.gltf");
        EnemyVisuals.MapImported("gloomwing_ray", "quaternius/flying/Hywirl.gltf");
        EnemyVisuals.MapImported("brine_mortar", "quaternius/flying/Goleling.gltf");
        EnemyVisuals.MapImported("thunderhead", "quaternius/flying/Goleling_Evolved.gltf");
        EnemyVisuals.MapImported("mistwalker", "quaternius/flying/Tribal.gltf");
        EnemyVisuals.MapImported("tempest_herald", "quaternius/flying/Armabee_Evolved.gltf");
        EnemyVisuals.MapImported("maelstrom_eye", "quaternius/flying/Glub_Evolved.gltf");
        EnemyVisuals.MapImported("gyre_matron", "quaternius/flying/Squidle.gltf");
        EnemyVisuals.MapImported("squall_tyrant", "quaternius/flying/Dragon.gltf");
        EnemyVisuals.MapImported("fogbound_stalker", "quaternius/big/Ninja.gltf");
        EnemyVisuals.MapImported("conch_drummer", "quaternius/big/Monkroose.gltf");
        EnemyVisuals.MapImported("coral_warlord", "quaternius/big/Tribal.gltf");
        EnemyVisuals.MapImported("tidepool_mender", "quaternius/blob/Wizard.gltf");
        EnemyVisuals.MapImported("sand_scuttler", "kenney/cube-pets/animal-crab.glb");

        Procedural("spume_mite", BodyType.Quadruped, s =>
        {
            s.Eyes = 4; s.Head = HeadGear.Antennae; s.Cadence = 2f; s.Tail = true;
            s.Accent = new Color(0.6f, 0.95f, 1f); s.Eye = new Color(0.3f, 0.9f, 1f);
        });
        Procedural("shellback_guard", BodyType.Biped, s =>
        {
            // The shield is the mechanic: it must be the first thing you see.
            s.LeftHand = HeldItem.Shield; s.RightHand = HeldItem.Spear; s.Head = HeadGear.Helmet;
            s.ShoulderPads = true; s.BackSpikes = true; s.Bulk = 1.15f; s.Eyes = 2;
            s.Accent = new Color(0.95f, 0.85f, 0.55f); s.Eye = new Color(1f, 0.7f, 0.3f);
        });
        Procedural("silt_eel", BodyType.Serpent, s =>
        {
            s.Eyes = 2; s.Mouth = true; s.Segments = 6; s.BackSpikes = true; s.Cadence = 1.4f;
            s.Accent = new Color(0.85f, 0.75f, 0.5f); s.Eye = new Color(1f, 0.9f, 0.4f);
        });
        Procedural("barnacle_hulk", BodyType.Brute, s =>
        {
            s.Crystals = true; s.ShoulderPads = true; s.Mouth = true; s.Eyes = 2; s.BackSpikes = true;
            s.Accent = new Color(0.85f, 0.88f, 0.8f); s.Eye = new Color(0.9f, 1f, 0.6f);
        });
        Procedural("carapace_bastion", BodyType.Brute, s =>
        {
            s.LeftHand = HeldItem.Shield; s.RightHand = HeldItem.Club; s.Head = HeadGear.CurvedHorns;
            s.ShoulderPads = true; s.Crystals = true; s.Eyes = 3; s.Jointed = true;
            s.Accent = new Color(1f, 0.7f, 0.6f); s.Eye = new Color(1f, 0.9f, 0.5f);
        });
    }

    /// <summary>Register an authored procedural body, with the same size and
    /// elite/boss accents <see cref="EnemyVisuals"/> gives its own.</summary>
    private static void Procedural(string id, BodyType body, System.Action<ModelSpec> dress)
    {
        if (Catalog.EnemyById(id) is not { } def) return;
        var s = new ModelSpec
        {
            Id = "enemy:" + id,
            Body = body,
            Primary = def.Tint,
            Secondary = def.Tint.Darkened(0.45f),
            Accent = def.Tint.Lightened(0.5f),
            Eye = new Color(1f, 0.92f, 0.45f),
            Face = false,
            Belly = true,
            Seed = (uint)id.GetHashCode(),
        };
        dress(s);
        s.Height = Mathf.Max(0.4f, def.Height);
        float defaultRatio = body switch { BodyType.Brute => 0.27f, BodyType.Blob => 0.45f, _ => 0.25f };
        s.Bulk *= Mathf.Clamp(def.Radius / Mathf.Max(0.1f, def.Height) / defaultRatio, 0.8f, 1.4f);
        if (def.IsElite || def.IsBoss)
        {
            s.Crown = def.IsElite;
            s.Aura = true;
            s.AuraColor = def.Tint.Lightened(0.25f);
            s.EyeGlow *= 1.4f;
            s.LodDistance = def.IsBoss ? 0f : 55f;
        }
        EnemyVisuals.Register(id, s, def.IsElite, def.IsBoss);
    }
}
