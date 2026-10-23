using System.Collections.Generic;
using Godot;

namespace Wavebreak.Visuals;

/// <summary>The skeleton family a model is built on. Each one has its own part
/// layout and its own locomotion in <see cref="CharacterRig"/>.</summary>
public enum BodyType
{
    /// <summary>Two legs, two arms, upright.</summary>
    Biped,
    /// <summary>Biped proportions pushed to the extreme: hunched, huge arms, short legs.</summary>
    Brute,
    /// <summary>Four legs, horizontal body, head out front.</summary>
    Quadruped,
    /// <summary>One squashy body that hops.</summary>
    Blob,
    /// <summary>Hovering body with flapping wings.</summary>
    Flyer,
    /// <summary>A head followed by a chain of segments that slither.</summary>
    Serpent,
}

public enum HeadGear { None, Horns, CurvedHorns, Antlers, Ears, LongEars, Antennae, Crest, Hood, Helmet, WizardHat, Visor }

public enum HeldItem { None, Sword, Club, Staff, Spear, Shield, Blades, Claws, Bow }

/// <summary>
/// A data description of one model. Everything a look needs is here, so new
/// enemies and characters are mostly new rows rather than new code — the same
/// rule the game applies to weapons and items.
///
/// Two ways to fill one in:
///   * procedural (the default): body type, palette and accessories, built by
///     <see cref="ModelBuilder"/> from primitives at first use and cached;
///   * imported: <see cref="FromScene"/> wraps a glTF/.tscn character with its
///     own AnimationPlayer. <see cref="CharacterRig"/> then plays the mapped clips
///     instead of posing parts, and the procedural fields act only as fallback.
///
/// Specs are compared by reference and their built meshes are cached on the
/// instance (<see cref="Built"/>), so a spec should be created once and reused —
/// <see cref="PlayerLooks"/> and <see cref="EnemyVisuals"/> both cache theirs.
/// </summary>
public sealed class ModelSpec
{
    public string Id = "model";
    public BodyType Body = BodyType.Biped;

    /// <summary>Standing height in metres, feet to top of head (not counting hats
    /// and horns). Every proportion is relative to it.</summary>
    public float Height = 1.6f;

    /// <summary>Width multiplier: 1 is a normal build, 1.4 is stocky.</summary>
    public float Bulk = 1f;

    /// <summary>Head size relative to the default (which is already chibi-large).</summary>
    public float HeadScale = 1f;

    public Color Primary = new(0.5f, 0.5f, 0.55f);
    public Color Secondary = new(0.3f, 0.3f, 0.35f);
    public Color Accent = new(0.9f, 0.8f, 0.3f);
    public Color Eye = new(1f, 0.95f, 0.6f);
    public Color Skin = new(0.95f, 0.78f, 0.62f);

    /// <summary>0 = no face, 1 = cyclops, 2 = normal, 3+ = spider row.</summary>
    public int Eyes = 2;
    public float EyeGlow = 1f;

    public HeadGear Head = HeadGear.None;
    public HeldItem RightHand = HeldItem.None;
    public HeldItem LeftHand = HeldItem.None;

    public bool Tail;
    public bool ShoulderPads;
    public bool BackSpikes;
    public bool Scarf;
    public bool Cape;
    public bool Belly = true;
    public bool Tusks;
    public bool Mouth;
    public bool Crystals;   // glowing shards growing out of the back/shoulders
    public bool Wings;      // bipeds and quadrupeds can have wings too; flyers always do
    public bool Face = true; // a skin-toned face plate under the eyes (heroes)

    /// <summary>Elite marker: a floating crown and a ground aura.</summary>
    public bool Crown;
    public bool Aura;
    public Color AuraColor = new(1f, 0.6f, 0.2f);

    /// <summary>Hero and boss quality: limbs get a knee/elbow joint, so a slide
    /// can fold the legs and a boss's swing can bend at the elbow.</summary>
    public bool Jointed;

    /// <summary>Number of trailing segments for serpents.</summary>
    public int Segments = 5;

    /// <summary>Strides per metre travelled. Small creatures scurry, big ones lumber.</summary>
    public float Cadence = 1f;

    /// <summary>Distance (m) beyond which the far LOD (one merged mesh, no limbs,
    /// no outline) is drawn instead. Bosses never switch.</summary>
    public float LodDistance = 34f;

    /// <summary>Seed for the small variations (rock knobs, spike angles).</summary>
    public uint Seed = 1;

    /// <summary>A hand-built body (see <see cref="CustomBody"/>, used by
    /// <c>CuteModels</c>): when set, the builder calls it instead of the body
    /// plan for <see cref="Body"/>, which then only picks the animation.</summary>
    public System.Action<CustomBody>? Custom;

    // --------------------------------------------------------------- imported

    /// <summary>An imported character scene. When set, the procedural fields above
    /// are ignored for building and the scene is instanced instead.</summary>
    public PackedScene? Scene;

    /// <summary>Clip names for the imported scene's AnimationPlayer, keyed by
    /// <see cref="AnimSlot"/>. Missing slots fall back sensibly (run → walk → idle).</summary>
    public Dictionary<AnimSlot, string> Clips = new();

    /// <summary>Uniform scale applied to the imported scene to reach <see cref="Height"/>.
    /// 0 means "measure the scene's AABB once and fit it".</summary>
    public float SceneScale;

    /// <summary>Yaw (radians) applied to the imported scene so it faces -Z.</summary>
    public float SceneYaw;

    /// <summary>
    /// Wrap an imported, animated character as a spec. Example:
    /// <code>
    /// ModelSpec.FromScene("res://art/monsters/Orc.gltf", height: 1.9f, clips: new() {
    ///     [AnimSlot.Idle] = "Idle", [AnimSlot.Walk] = "Walk", [AnimSlot.Run] = "Run",
    ///     [AnimSlot.Attack] = "Punch", [AnimSlot.Hit] = "HitReact", [AnimSlot.Death] = "Death" });
    /// </code>
    /// Returns null if the scene cannot be loaded, so callers can fall back to a
    /// procedural spec with <c>?? proceduralSpec</c>.
    /// </summary>
    public static ModelSpec? FromScene(string path, float height, Dictionary<AnimSlot, string>? clips = null,
                                       float yaw = 0f, string? id = null)
    {
        if (!ResourceLoader.Exists(path)) return null;
        if (ResourceLoader.Load(path) is not PackedScene scene) return null;
        return FromScene(scene, height, clips, yaw, id ?? path);
    }

    public static ModelSpec FromScene(PackedScene scene, float height, Dictionary<AnimSlot, string>? clips = null,
                                      float yaw = 0f, string? id = null) => new()
    {
        Id = id ?? scene.ResourcePath,
        Scene = scene,
        Height = height,
        Clips = clips ?? new Dictionary<AnimSlot, string>(),
        SceneYaw = yaw,
    };

    public bool IsImported => Scene is not null;

    /// <summary>Imported models: planar speed (m/s) above which the run clip
    /// replaces the walk. Slow enemies never "run", whatever the director's
    /// speed scaling does to them.</summary>
    public float RunThreshold = 4.6f;

    /// <summary>Built meshes, filled on first use by <see cref="ModelBuilder"/>.</summary>
    internal BuiltModel? Built;

    /// <summary>A copy to edit. The cached meshes and the clip map are not shared:
    /// a clone is meant to be changed and built separately.</summary>
    public ModelSpec Clone(string? id = null)
    {
        var c = (ModelSpec)MemberwiseClone();
        c.Built = null;
        c.Clips = new Dictionary<AnimSlot, string>(Clips);
        if (id is not null) c.Id = id;
        return c;
    }
}

/// <summary>Animation slots for imported scenes.</summary>
public enum AnimSlot { Idle, Walk, Run, Attack, Windup, Hit, Death, Jump, Fall, Dash, Slide, Slam }
