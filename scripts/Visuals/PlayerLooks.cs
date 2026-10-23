using System.Collections.Generic;
using Godot;

namespace Wavebreak.Visuals;

/// <summary>
/// The hero's looks, by id. Character selection maps a playable character to
/// one of these ids; the player builds its body from <c>PlayerLooks.Get(id)</c>.
///
/// Every look shares the hero skeleton (jointed biped, 1.8 m, big readable head)
/// so the controller's poses — slide, slam tuck, dash lean, flip — fit all of
/// them. What changes is silhouette at a glance: hat, ears, weapon, bulk. At the
/// gameplay camera's distance the silhouette is what tells two characters apart;
/// colour alone does not survive a screen full of enemies.
/// </summary>
public static partial class PlayerLooks
{
    public const string Default = "default";

    private static readonly Dictionary<string, ModelSpec> Looks = new();

    /// <summary>Every look id, in display order. The first eight are the imported
    /// Kenney heroes; the <c>proc-</c> ones are the procedural builds, kept as the
    /// fallback and as looks in their own right.</summary>
    public static readonly string[] Ids =
    {
        "default", "default_tidecaller", "ember", "ember_forgeborn", "gale", "gale_stormchaser", "augur", "augur_eclipse",
        "vanguard", "arcanist", "wildling", "golem", "scout", "medic", "rogue",
        "proc-default", "proc-vanguard", "proc-arcanist", "proc-wildling", "proc-golem",
    };

    /// <summary>Imported model per look id (mini-characters pack). A missing file
    /// falls back to the procedural look of the same name.</summary>
    private static readonly Dictionary<string, string> Imported = new()
    {
        // Character skins (base, unlockable) — each its own model, so a skin
        // reads as a different person at gameplay distance, not a recolour.
        ["default"] = "kenney/mini-characters/character-male-a.glb",            // drifter
        ["default_tidecaller"] = "kenney/mini-characters/character-male-b.glb",
        ["ember"] = "kenney/mini-characters/character-female-b.glb",           // kiln sister
        ["ember_forgeborn"] = "kenney/mini-characters/character-female-e.glb",
        ["gale"] = "kenney/mini-characters/character-female-a.glb",            // gale courier
        ["gale_stormchaser"] = "kenney/mini-characters/character-female-c.glb",
        ["augur"] = "kenney/mini-characters/character-male-e.glb",             // gilded augur
        ["augur_eclipse"] = "kenney/mini-characters/character-male-f.glb",
        // Further looks, unassigned to a character yet.
        ["vanguard"] = "kenney/mini-characters/character-male-c.glb",
        ["scout"] = "kenney/mini-characters/character-female-f.glb",
        ["medic"] = "kenney/mini-characters/character-female-d.glb",
        ["rogue"] = "kenney/mini-characters/character-male-d.glb",
    };

    /// <summary>The spec for <paramref name="id"/>; unknown ids get the default
    /// look rather than failing, so a character added before its look still plays.</summary>
    public static ModelSpec Get(string? id)
    {
        id = string.IsNullOrEmpty(id) ? Default : id;
        if (Looks.TryGetValue(id, out ModelSpec? spec)) return spec;
        spec = Resolve(id) ?? Resolve(Default)!;
        Looks[id] = spec;
        return spec;
    }

    /// <summary>
    /// A ready-to-add, idling model of a look — for menus, character select and
    /// unlock toasts. Origin at the feet, facing -Z; it animates itself (turn it
    /// with the returned node's rotation). Wraps a <see cref="CharacterRig"/>.
    /// </summary>
    public static Node3D Build(string? lookId) => Build(lookId, SkinRecolor.None);

    /// <summary><see cref="Build(string?)"/> in a skin's colours (see
    /// <see cref="SkinRecolor"/>) — the menu preview uses this, so what the
    /// skin picker shows is what the run wears.</summary>
    public static Node3D Build(string? lookId, SkinRecolor recolor)
    {
        CharacterRig rig = CharacterRig.Create(Get(lookId));
        rig.ManualMotion = true; // stands still: idle breathing / idle clip
        var holder = new Node3D { Name = "Look_" + (lookId ?? Default) };
        holder.AddChild(rig);
        rig.SetRecolor(recolor);
        return holder;
    }

    private static ModelSpec? Resolve(string id)
    {
        if (id.StartsWith("proc-")) return Make(id["proc-".Length..]);
        if ((Imported.TryGetValue(id, out string? file) || ExpansionLooks.TryGetValue(id, out file))
            && ArtAssets.Model(file, 1.75f, ArtAssets.ClipsFor(file), "hero:" + id) is { } imported)
        {
            imported.LodDistance = 0f;
            return imported;
        }
        return Make(id) ?? Make(Default);
    }

    /// <summary>Register or replace a look — e.g. an imported glTF hero via
    /// <see cref="ModelSpec.FromScene(string, float, Dictionary{AnimSlot, string}?, float, string?)"/>.</summary>
    public static void Register(string id, ModelSpec spec) => Looks[id] = spec;

    private static ModelSpec Hero(string id) => new()
    {
        Id = "hero:" + id,
        Body = BodyType.Biped,
        Height = 1.8f,
        Jointed = true,
        HeadScale = 1.12f,
        Face = true,
        LodDistance = 0f, // the hero is always close
    };

    private static ModelSpec? Make(string id)
    {
        ModelSpec s = Hero(id);
        switch (id)
        {
            case "default":
                // The runner: light, fast-looking, a scarf that trails. Cyan and
                // navy so it never shares a palette with anything that attacks it.
                s.Primary = new Color(0.22f, 0.62f, 0.95f);
                s.Secondary = new Color(0.14f, 0.18f, 0.34f);
                s.Accent = new Color(1.0f, 0.78f, 0.25f);
                s.Eye = new Color(1f, 1f, 1f);
                s.Head = HeadGear.Crest;
                s.Scarf = true;
                s.RightHand = HeldItem.Sword;
                break;
            case "vanguard":
                // Armoured knight: helmet with a glowing visor slit, shield and sword.
                s.Primary = new Color(0.72f, 0.74f, 0.8f);
                s.Secondary = new Color(0.36f, 0.22f, 0.2f);
                s.Accent = new Color(0.9f, 0.25f, 0.2f);
                s.Eye = new Color(0.55f, 0.9f, 1f);
                s.Head = HeadGear.Helmet;
                s.Face = false;
                s.Eyes = 0;
                s.ShoulderPads = true;
                s.Bulk = 1.15f;
                s.RightHand = HeldItem.Sword;
                s.LeftHand = HeldItem.Shield;
                s.Cape = true;
                break;
            case "arcanist":
                // Robed caster: tall hat, glowing staff.
                s.Primary = new Color(0.45f, 0.28f, 0.72f);
                s.Secondary = new Color(0.22f, 0.14f, 0.4f);
                s.Accent = new Color(0.4f, 1f, 0.85f);
                s.Head = HeadGear.WizardHat;
                s.RightHand = HeldItem.Staff;
                s.Cape = true;
                s.Belly = false;
                break;
            case "wildling":
                // Fox-eared ranger with a tail and a bow.
                s.Primary = new Color(0.95f, 0.52f, 0.2f);
                s.Secondary = new Color(0.3f, 0.42f, 0.22f);
                s.Accent = new Color(1f, 0.95f, 0.85f);
                s.Head = HeadGear.LongEars;
                s.Tail = true;
                s.LeftHand = HeldItem.Bow;
                s.Skin = new Color(1f, 0.88f, 0.75f);
                break;
            case "golem":
                // Stone bruiser with glowing crystal seams.
                s.Primary = new Color(0.5f, 0.52f, 0.58f);
                s.Secondary = new Color(0.32f, 0.33f, 0.38f);
                s.Accent = new Color(0.35f, 0.95f, 1f);
                s.Eye = new Color(0.35f, 0.95f, 1f);
                s.Face = false;
                s.Bulk = 1.35f;
                s.HeadScale = 0.95f;
                s.Crystals = true;
                s.ShoulderPads = true;
                s.Belly = false;
                break;
            default:
                return null;
        }
        return s;
    }
}
