using System.Collections.Generic;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Visuals;

/// <summary>
/// Enemy definitions to models.
///
/// Known enemies get an authored spec by id. Anything else — an enemy added
/// next week — is classified from its numbers the same way the old primitive
/// bodies were (fast = low runner, tough = brute, elite/boss flags), so a new
/// definition always gets a sensible model and nobody has to remember to come
/// here. Size, colour and the elite/boss accents always come from the def, so a
/// retuned enemy's model follows its tuning.
///
/// Integration for <c>Enemy.cs</c> is four calls:
/// <code>
///   _rig = EnemyVisuals.Attach(this, def, _rig);   // when configured (reuses if unchanged)
///   _rig?.PlayHit();                                // when damaged
///   _rig?.PlayAttack();                             // when it strikes
///   EnemyVisuals.SpawnCorpse(_rig);                 // when it dies (the node is pooled at once)
/// </code>
/// </summary>
public static class EnemyVisuals
{
    private static readonly Dictionary<string, ModelSpec> Specs = new();

    /// <summary>Build a fresh model for <paramref name="def"/>. Faces +Z, which
    /// is how enemies turn.</summary>
    public static CharacterRig Build(EnemyDef def)
    {
        ModelSpec spec = SpecFor(def);
        CharacterRig rig = CharacterRig.Create(spec, faceZ: true, outline: true);
        rig.AttackCooldown = def.AttackCooldown;
        return rig;
    }

    /// <summary>
    /// Give <paramref name="owner"/> the model for <paramref name="def"/>. If it
    /// already wears that model, the existing rig is reset and returned (pooled
    /// enemies are reconfigured on every spawn, and rebuilding would throw away
    /// the whole point of pooling). Returns null when models are switched off
    /// (<see cref="ArtAssets.ModelsEnabled"/>), in which case the enemy keeps
    /// its own primitive body.
    /// </summary>
    public static CharacterRig? Attach(Node3D owner, EnemyDef def, CharacterRig? current)
    {
        if (!ArtAssets.ModelsEnabled)
        {
            if (current is not null && GodotObject.IsInstanceValid(current)) Retire(current);
            return null;
        }
        ModelSpec spec = SpecFor(def);
        if (current is not null && GodotObject.IsInstanceValid(current) && current.Spec == spec)
        {
            current.ResetState();
            return current;
        }
        if (current is not null && GodotObject.IsInstanceValid(current)) Retire(current);
        CharacterRig rig = Build(def);
        owner.AddChild(rig);
        return rig;
    }

    private static void Retire(CharacterRig rig)
    {
        rig.GetParent()?.RemoveChild(rig);
        rig.QueueFree();
    }

    /// <summary>The spec for a definition, cached per id.</summary>
    public static ModelSpec SpecFor(EnemyDef def)
    {
        if (Game.Arena.Current.Underwater && def.Id == ReefLeviathanModels.Id) return ReefLeviathanModels.For(def);
        if (Game.Arena.Current.Underwater && def.Id == ReefGuardianModels.Id) return ReefGuardianModels.For(def);
        if (CityGiantModels.IsGiant(def)) return CityGiantModels.For(def);
        if (Game.Arena.Current.Underwater) return ReefMarineModels.For(def);
        string key = def.Id + (def.IsElite ? "+e" : "") + (def.IsBoss ? "+b" : "");
        if (Specs.TryGetValue(key, out ModelSpec? s)) return s;
        s = ImportedFor(def) ?? Authored(def) ?? Classified(def);
        Finish(s, def);
        Specs[key] = s;
        return s;
    }

    /// <summary>Replace the model for an enemy id — e.g. with an imported glTF
    /// monster from <see cref="ModelSpec.FromScene(string, float, Dictionary{AnimSlot, string}?, float, string?)"/>.</summary>
    public static void Register(string enemyId, ModelSpec spec, bool elite = false, bool boss = false)
        => Specs[enemyId + (elite ? "+e" : "") + (boss ? "+b" : "")] = spec;

    // ---------------------------------------------------------------- specs

    /// <summary>
    /// Imported model per enemy id. Chosen for silhouette first — the swarm is
    /// small and low, brutes are wide, bosses tower — and for flavour second.
    /// Enemies without an entry use the procedural models below.
    /// </summary>
    private static readonly Dictionary<string, string> Imported = new()
    {
        // Swarm: Quaternius blobs — small, round, readable as a mass.
        ["mote"] = "quaternius/blob/GreenBlob.gltf",
        ["husk"] = "quaternius/blob/Mushnub.gltf",
        ["skitterling"] = "quaternius/blob/Birb.gltf",
        ["brine_leech"] = "quaternius/blob/Fish.gltf",
        ["kelp_shambler"] = "quaternius/blob/Cactoro.gltf",
        // Runners and fliers.
        ["cinderling"] = "quaternius/flying/Armabee.gltf",
        ["gloom_lurcher"] = "quaternius/big/Dino.gltf",
        // Brutes: the Big set's humanoids.
        ["gravel_brute"] = "quaternius/big/Orc.gltf",
        ["rift_maw"] = "quaternius/big/Alien.gltf",
        ["tidewall_colossus"] = "quaternius/big/Frog.gltf",
        // Kenney's skeleton, for variety among the brutes.
        ["pale_sentinel"] = "kenney/graveyard-kit/character-skeleton.glb",
        // Elites.
        ["ash_warden"] = "quaternius/big/Demon.gltf",
        ["frost_herald"] = "quaternius/big/Yeti.gltf",
        ["gale_revenant"] = "quaternius/flying/Ghost.gltf",
        // Bosses: the biggest silhouettes in the set, scaled to boss height.
        ["thresher_prime"] = "quaternius/big/Orc_Skull.gltf",
        ["breaker_matriarch"] = "quaternius/big/MushroomKing.gltf",
    };

    /// <summary>Map an enemy id to an imported model (or replace a mapping).
    /// Path is relative to <see cref="ArtAssets.Root"/>.</summary>
    public static void MapImported(string enemyId, string relativePath)
    {
        Imported[enemyId] = relativePath;
        Specs.Remove(enemyId);
        Specs.Remove(enemyId + "+e");
        Specs.Remove(enemyId + "+b");
    }

    private static ModelSpec? ImportedFor(EnemyDef def)
    {
        if (!Imported.TryGetValue(def.Id, out string? file)) return null;
        ModelSpec? s = ArtAssets.Model(file, def.Height, ArtAssets.ClipsFor(file), "enemy:" + def.Id);
        if (s is null) return null;
        // Pets read as cute at their native colours; the definition's tint goes
        // on as a ground aura only for elites and bosses (Finish), so ordinary
        // enemies keep the artist's palette.
        s.RunThreshold = def.MoveSpeed >= 5f ? 3.5f : 99f;
        return s;
    }

    private static ModelSpec Base(EnemyDef def, BodyType body) => new()
    {
        Id = "enemy:" + def.Id,
        Body = body,
        Primary = def.Tint,
        Secondary = def.Tint.Darkened(0.45f),
        Accent = def.Tint.Lightened(0.5f),
        Eye = new Color(1f, 0.92f, 0.45f),
        Face = false,
        Belly = true,
        Seed = (uint)def.Id.GetHashCode(),
    };

    private static ModelSpec? Authored(EnemyDef def)
    {
        ModelSpec s;
        switch (def.Id)
        {
            case "mote":
                s = Base(def, BodyType.Blob);
                s.Eyes = 1; s.Head = HeadGear.Antennae; s.Accent = new Color(0.85f, 1f, 0.5f);
                s.Eye = new Color(1f, 1f, 0.75f);
                break;
            case "husk":
                s = Base(def, BodyType.Biped);
                s.Head = HeadGear.Hood; s.Secondary = new Color(0.32f, 0.22f, 0.18f);
                s.RightHand = HeldItem.Club; s.Eye = new Color(1f, 0.55f, 0.2f); s.Belly = false;
                break;
            case "skitterling":
                s = Base(def, BodyType.Quadruped);
                s.Eyes = 4; s.Head = HeadGear.Antennae; s.Cadence = 1.8f; s.BackSpikes = true;
                s.Accent = new Color(1f, 0.5f, 0.2f); s.Eye = new Color(1f, 0.25f, 0.2f);
                break;
            case "gravel_brute":
                s = Base(def, BodyType.Brute);
                s.ShoulderPads = true; s.BackSpikes = true; s.Eyes = 2; s.Mouth = true;
                s.Secondary = new Color(0.3f, 0.32f, 0.4f); s.Accent = new Color(0.75f, 0.78f, 0.86f);
                s.Eye = new Color(1f, 0.8f, 0.3f);
                break;
            case "cinderling":
                s = Base(def, BodyType.Flyer);
                s.Eyes = 2; s.Head = HeadGear.Horns; s.Crystals = true;
                s.Accent = new Color(1f, 0.85f, 0.3f); s.Eye = new Color(1f, 1f, 0.6f); s.EyeGlow = 1.5f;
                break;
            case "pale_sentinel":
                s = Base(def, BodyType.Biped);
                s.Head = HeadGear.Helmet; s.Eyes = 0; s.ShoulderPads = true; s.Bulk = 1.15f;
                s.RightHand = HeldItem.Spear; s.LeftHand = HeldItem.Shield; s.Cape = true;
                s.Secondary = new Color(0.45f, 0.42f, 0.6f); s.Accent = new Color(0.95f, 0.85f, 0.5f);
                s.Eye = new Color(0.6f, 0.9f, 1f);
                break;
            case "rift_maw":
                s = Base(def, BodyType.Blob);
                s.Eyes = 5; s.Mouth = true; s.Tusks = true; s.Head = HeadGear.CurvedHorns; s.Bulk = 1.15f;
                s.Accent = new Color(1f, 0.6f, 0.9f); s.Eye = new Color(1f, 0.95f, 0.5f);
                break;
            case "ash_warden":
                s = Base(def, BodyType.Biped);
                s.Head = HeadGear.CurvedHorns; s.RightHand = HeldItem.Staff; s.Cape = true; s.ShoulderPads = true;
                s.Secondary = new Color(0.3f, 0.16f, 0.12f); s.Accent = new Color(1f, 0.75f, 0.3f);
                s.Eye = new Color(1f, 0.85f, 0.3f); s.Jointed = true;
                break;
            case "frost_herald":
                s = Base(def, BodyType.Flyer);
                s.Eyes = 3; s.Crystals = true; s.Head = HeadGear.Crest; s.Bulk = 1.1f;
                s.Accent = new Color(0.75f, 0.97f, 1f); s.Eye = new Color(0.85f, 1f, 1f);
                break;
            case "thresher_prime":
                s = Base(def, BodyType.Brute);
                s.Eyes = 3; s.Head = HeadGear.Horns; s.BackSpikes = true; s.ShoulderPads = true; s.Mouth = true;
                s.RightHand = HeldItem.Blades; s.LeftHand = HeldItem.Blades; s.Jointed = true;
                s.Secondary = new Color(0.25f, 0.08f, 0.1f); s.Accent = new Color(1f, 0.75f, 0.55f);
                s.Eye = new Color(1f, 0.95f, 0.5f);
                break;
            case "brine_leech":
                s = Base(def, BodyType.Serpent);
                s.Eyes = 2; s.Mouth = true; s.Segments = 4; s.BackSpikes = true; s.Cadence = 1.6f;
                s.Accent = new Color(0.7f, 1f, 0.9f);
                break;
            case "kelp_shambler":
                s = Base(def, BodyType.Biped);
                s.Head = HeadGear.Crest; s.Tusks = true; s.BackSpikes = true; s.Bulk = 1.1f;
                s.Accent = new Color(0.55f, 0.85f, 0.4f); s.Eye = new Color(0.85f, 1f, 0.5f);
                s.RightHand = HeldItem.Claws; s.LeftHand = HeldItem.Claws;
                break;
            case "gloom_lurcher":
                s = Base(def, BodyType.Quadruped);
                s.Head = HeadGear.Horns; s.Tail = true; s.Eyes = 2; s.Mouth = true; s.Crystals = true;
                s.Accent = new Color(0.7f, 0.5f, 1f); s.Eye = new Color(0.85f, 0.6f, 1f); s.EyeGlow = 1.6f;
                break;
            case "tidewall_colossus":
                s = Base(def, BodyType.Brute);
                s.ShoulderPads = true; s.Crystals = true; s.LeftHand = HeldItem.Shield; s.Eyes = 1;
                s.Head = HeadGear.Crest; s.Accent = new Color(0.55f, 0.95f, 0.9f); s.Eye = new Color(0.6f, 1f, 0.95f);
                break;
            case "gale_revenant":
                s = Base(def, BodyType.Flyer);
                s.Head = HeadGear.Hood; s.Eyes = 2; s.Secondary = new Color(0.4f, 0.55f, 0.52f);
                s.Accent = new Color(0.8f, 1f, 0.92f); s.Eye = new Color(0.7f, 1f, 0.85f); s.EyeGlow = 1.8f;
                break;
            case "breaker_matriarch":
                s = Base(def, BodyType.Serpent);
                s.Eyes = 4; s.Mouth = true; s.Head = HeadGear.CurvedHorns; s.Segments = 7; s.BackSpikes = true;
                s.Crystals = true; s.Accent = new Color(0.6f, 0.95f, 1f); s.Eye = new Color(0.9f, 1f, 0.6f);
                break;
            default:
                return null;
        }
        return s;
    }

    /// <summary>The fallback: read a body plan off the numbers.</summary>
    private static ModelSpec Classified(EnemyDef def)
    {
        BodyType body = def.IsBoss ? BodyType.Brute
                      : def.MoveSpeed >= 5f ? (def.Height < 1.1f ? BodyType.Serpent : BodyType.Quadruped)
                      : def.Height < 1.15f ? BodyType.Blob
                      : def.Health >= 60f || def.KnockbackResist >= 0.5f ? BodyType.Brute
                      : BodyType.Biped;
        ModelSpec s = Base(def, body);
        s.Mouth = body is BodyType.Blob or BodyType.Brute;
        s.BackSpikes = body is BodyType.Brute or BodyType.Quadruped;
        s.Head = body == BodyType.Quadruped ? HeadGear.Ears : body == BodyType.Brute ? HeadGear.Horns : HeadGear.None;
        s.Tail = body == BodyType.Quadruped;
        if (def.IsBoss) { s.Eyes = 3; s.Jointed = true; s.RightHand = HeldItem.Club; s.ShoulderPads = true; }
        return s;
    }

    /// <summary>What every enemy model takes from its definition regardless of
    /// how it was chosen: size, and the elite/boss accents.</summary>
    private static void Finish(ModelSpec s, EnemyDef def)
    {
        s.Height = Mathf.Max(0.4f, def.Height);
        // Wider collision than the default proportions means a stockier model.
        float defaultRatio = s.Body switch { BodyType.Brute => 0.27f, BodyType.Blob => 0.45f, _ => 0.25f };
        s.Bulk *= Mathf.Clamp(def.Radius / Mathf.Max(0.1f, def.Height) / defaultRatio, 0.8f, 1.4f);
        if (def.IsElite || def.IsBoss)
        {
            s.Crown = def.IsElite;
            s.Aura = true;
            s.AuraColor = def.Tint.Lightened(0.25f);
            s.EyeGlow *= 1.4f;
        }
        if (def.IsBoss)
        {
            s.LodDistance = 0f; // a boss is always worth the full model
            s.Jointed = true;
        }
        if (def.IsElite) s.LodDistance = 55f;
    }

    // -------------------------------------------------------------- corpses

    private static readonly Dictionary<ModelSpec, Stack<CharacterRig>> CorpsePool = new();
    private static int _activeCorpses;
    private static Node? _world;
    private const int MaxCorpses = 36;

    /// <summary>
    /// Play the death of <paramref name="rig"/> on a stand-in body.
    ///
    /// Enemies return to their pool (hidden, processing off) the instant they
    /// die, so their own rig cannot be the one that crumples. A pooled corpse of
    /// the same model takes over at the same transform and dissolves. Capped,
    /// because a screen-clearing explosion should not cost forty extra models —
    /// past the cap, deaths simply pop.
    /// </summary>
    public static void SpawnCorpse(CharacterRig? rig)
    {
        if (rig is null || !GodotObject.IsInstanceValid(rig) || !rig.IsInsideTree()) return;
        if (_activeCorpses >= MaxCorpses) return;
        Node? world = rig.GetTree().CurrentScene;
        if (world is null) return;
        if (world != _world)
        {
            // A restarted run: the old pool died with the old scene.
            ResetCorpses();
            _world = world;
        }

        ModelSpec spec = rig.Spec;
        if (!CorpsePool.TryGetValue(spec, out Stack<CharacterRig>? stack))
            CorpsePool[spec] = stack = new Stack<CharacterRig>();

        CharacterRig? corpse = null;
        while (stack.Count > 0 && corpse is null)
        {
            CharacterRig c = stack.Pop();
            if (GodotObject.IsInstanceValid(c)) corpse = c;
        }
        if (corpse is null)
        {
            corpse = CharacterRig.Create(spec, faceZ: false, outline: true);
            corpse.DeathFinished += c =>
            {
                _activeCorpses--;
                c.ProcessMode = Node.ProcessModeEnum.Disabled;
                if (CorpsePool.TryGetValue(c.Spec, out var s)) s.Push(c);
            };
            world.AddChild(corpse);
        }
        else if (corpse.GetParent() != world)
        {
            corpse.GetParent()?.RemoveChild(corpse);
            world.AddChild(corpse);
        }

        _activeCorpses++;
        corpse.ProcessMode = Node.ProcessModeEnum.Inherit;
        corpse.ResetState();
        corpse.GlobalTransform = rig.GlobalTransform;
        corpse.PlayDeath();
    }

    /// <summary>Forget pooled corpses (scene change). They are children of the
    /// old scene and die with it.</summary>
    public static void ResetCorpses()
    {
        CorpsePool.Clear();
        _activeCorpses = 0;
    }
}
