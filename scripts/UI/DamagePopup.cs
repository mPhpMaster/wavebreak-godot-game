using System.Collections.Generic;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;

namespace Wavebreak.UI;

/// <summary>
/// The floating-number layer.
///
/// Subscribes to <see cref="PopupRequested"/> and draws whatever anyone asks for
/// at a world position: damage, heals, dodges, and the director's wave
/// announcements. Nothing in combat holds a reference to this node — the bus is
/// the entire interface, which is what lets the whole layer be deleted for a
/// headless test without touching a line of gameplay code.
///
/// Three decisions worth knowing about.
///
/// Labels are pooled. A busy second produces well over a hundred numbers; each
/// one is a <see cref="Label3D"/>, and one created and freed per hit means a
/// mesh rebuild and a scene-tree insertion for something that lives for three
/// quarters of a second.
///
/// They are driven from one <c>_Process</c> here rather than each running its
/// own. Same reasoning as <see cref="Combat.Projectile"/>: a hundred managed
/// callbacks a frame costs more than the arithmetic they perform.
///
/// When the pool is full the *oldest* popup is recycled rather than the new
/// request being dropped. A number that never appears reads to the player as
/// "that hit did nothing", and the hit most worth showing is always the newest.
/// </summary>
public sealed partial class DamagePopup : Node3D
{
    /// <summary>Ceiling on numbers on screen at once. Past this the display is
    /// unreadable anyway, so the cap costs nothing the player can perceive.</summary>
    private const int Capacity = 128;

    /// <summary>Scale a request must reach to be treated as emphatic — a crit, a
    /// boss announcement. Publishers express importance through
    /// <see cref="PopupRequested.Scale"/>, and this is the threshold that turns
    /// it into a heavier outline, a bigger body and a longer read.</summary>
    public const float EmphasisScale = 1.25f;

    /// <summary>Distinct offsets a burst of popups is fanned across. A power of
    /// two, so the cycling index is a mask rather than a modulo.</summary>
    private const int FanSlots = 16;

    private readonly List<Popup> _live = new(Capacity);
    private Pool<Popup>? _pool;
    private int _spawnIndex;
    private bool _subscribed;

    /// <summary>Numbers currently on screen. For debug overlays and tests.</summary>
    public int ActiveCount => _live.Count;

    public override void _Ready()
    {
        Name = "DamagePopups";

        // Deliberately unaffected by the pause the level-up panel applies. A
        // number frozen half-way through its rise for as long as the player takes
        // to read three cards looks like a stuck object; popups finish their
        // animation and clear the screen instead.
        ProcessMode = ProcessModeEnum.Always;

        _pool = new Pool<Popup>(this, static () => new Popup(), Capacity, prewarm: 32);

        EventBus.Subscribe<PopupRequested>(OnRequested);
        _subscribed = true;
    }

    public override void _ExitTree()
    {
        if (_subscribed)
        {
            EventBus.Unsubscribe<PopupRequested>(OnRequested);
            _subscribed = false;
        }

        _live.Clear();
        _pool?.Clear();
        _pool = null;
    }

    // ---------------------------------------------------------------- spawn

    private void OnRequested(PopupRequested request)
    {
        if (string.IsNullOrEmpty(request.Text)) return;

        Popup? popup = _pool?.Get();
        if (popup is null)
        {
            // Pool exhausted. Steal the oldest live entry: it has had the most
            // time on screen and is the least likely to still be being read.
            if (_live.Count == 0) return;
            popup = _live[0];
            _live.RemoveAt(0);
        }

        // Fan successive popups around a circle. Without it, two hits on the same
        // enemy in the same frame draw exactly on top of each other and read as
        // one number at double brightness.
        //
        // Two rings rather than one, alternating: from the game's near-overhead
        // camera a single ring still projects to a flat line, so half its points
        // land on top of each other. Staggering the height as well separates the
        // dense case — a boss taking six weapons at once — into readable rows.
        int slot = _spawnIndex++ & (FanSlots - 1);
        float angle = slot * Mathf.Tau / FanSlots;
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);
        float ring = (slot & 1) == 0 ? 0.30f : 0.64f;

        Vector3 at = request.Position + new Vector3(cos * ring, (slot & 3) * 0.07f, sin * ring);
        Vector3 drift = new(cos * 0.55f, 0f, sin * 0.55f);

        popup.Begin(at, drift, request.Text, request.Color, request.Scale);
        _live.Add(popup);
    }

    // ----------------------------------------------------------------- step

    public override void _Process(double delta)
    {
        if (_live.Count == 0) return;

        float dt = (float)delta;

        // Backwards, so removing an entry cannot skip the one that fills the hole.
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Popup popup = _live[i];

            if (!IsInstanceValid(popup))
            {
                _live.RemoveAt(i);
                continue;
            }

            if (popup.Step(dt)) continue;

            _live.RemoveAt(i);
            _pool?.Release(popup);
        }
    }

    // ------------------------------------------------------ publish helpers
    // Convenience for the combat side so that every weapon does not invent its
    // own colour scheme. Publishing PopupRequested directly stays perfectly
    // legal; these exist only so the common cases look the same as each other.

    private static readonly Color CritColor = new(1.00f, 0.86f, 0.28f);

    /// <summary>Colour a damage number is drawn in for its element. Bright enough
    /// to read against the arena's dark floor, and separated by hue rather than
    /// by brightness so they stay distinguishable for a colour-blind player.</summary>
    public static Color ElementColor(Element element) => element switch
    {
        Element.Fire      => new Color(1.00f, 0.54f, 0.20f),
        Element.Ice       => new Color(0.56f, 0.88f, 1.00f),
        Element.Lightning => new Color(0.96f, 0.92f, 0.42f),
        Element.Poison    => new Color(0.62f, 0.95f, 0.38f),
        _                 => new Color(0.94f, 0.95f, 0.98f),
    };

    /// <summary>Ask for a damage number over a target. Crits are drawn in gold
    /// above <see cref="EmphasisScale"/>, which is what selects the heavier
    /// treatment on the label itself.</summary>
    public static void Damage(Vector3 at, float amount, bool crit = false, Element element = Element.Physical)
    {
        if (amount < 0.5f) return;

        EventBus.Publish(new PopupRequested(
            at,
            Mathf.RoundToInt(amount).ToString(),
            crit ? CritColor : ElementColor(element),
            crit ? 1.45f : 1f));
    }

    /// <summary>Ask for a line of text over a position — pickups, statuses,
    /// anything that is a word rather than a number.</summary>
    public static void Say(Vector3 at, string text, Color color, float scale = 1f)
    {
        if (string.IsNullOrEmpty(text)) return;
        EventBus.Publish(new PopupRequested(at, text, color, scale));
    }
}

/// <summary>
/// One floating label.
///
/// A <see cref="Label3D"/> subclass rather than a wrapper node holding one: the
/// wrapper would double the node count for no gain, and Label3D is already a
/// <see cref="Node3D"/>, which is all <see cref="Pool{T}"/> asks for.
///
/// Billboarded and depth-test-free on purpose. A number occluded by the enemy
/// that produced it is a number that does not exist, and the entire point of the
/// layer is feedback the player cannot miss.
/// </summary>
internal sealed partial class Popup : Label3D, IPooled
{
    /// <summary>Font size the labels are rasterised at. Kept large and scaled
    /// down through <see cref="Label3D.PixelSize"/> so the glyph atlas is sharp
    /// at the sizes crits reach instead of being upscaled into mush.</summary>
    private const int FontPixels = 64;

    /// <summary>World metres per font pixel. Tuned against the camera's default
    /// 9.5m arm: an ordinary number lands around twenty screen pixels tall at
    /// 1280x720, which is legible without covering what it describes.</summary>
    private const float BasePixelSize = 0.0052f;

    private const float Lifetime = 0.78f;
    private const float EmphasisLifetime = 1.05f;

    /// <summary>How fast the rise bleeds off, per second. Popups decelerate
    /// rather than arc, so they settle at the top of their climb instead of
    /// falling back through the thing that produced them.</summary>
    private const float Damping = 2.4f;

    private static readonly Color OutlineBase = new(0.03f, 0.03f, 0.05f, 1f);

    private Vector3 _velocity;
    private Color _color = Colors.White;
    private float _age;
    private float _life = Lifetime;
    private float _scale = 1f;

    /// <inheritdoc/>
    public bool IsPooled { get; set; } = true;

    public Popup()
    {
        // Driven by DamagePopup's loop; see that class's note.
        SetProcess(false);
        SetPhysicsProcess(false);
        Visible = false;

        FontSize = FontPixels;
        PixelSize = BasePixelSize;
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        Shaded = false;
        DoubleSided = true;
        NoDepthTest = true;
        FixedSize = false;
        HorizontalAlignment = Godot.HorizontalAlignment.Center;
        VerticalAlignment = Godot.VerticalAlignment.Center;

        // Drawn after the world and after each other in request order, so the
        // newest number sits on top of the ones already fading out.
        RenderPriority = 2;
        OutlineRenderPriority = 1;
        OutlineModulate = OutlineBase;
    }

    /// <summary>Reset and place this label for a new request.</summary>
    /// <param name="position">Where the number starts, in world space.</param>
    /// <param name="drift">Horizontal spread direction, supplied by the layer so
    /// that a burst fans out instead of forming a column that hides its own
    /// lower half.</param>
    /// <param name="text">What to draw. Numbers and short words both.</param>
    /// <param name="color">Base tint; alpha is overwritten by the fade.</param>
    /// <param name="scale">Emphasis. See <see cref="DamagePopup.EmphasisScale"/>.</param>
    public void Begin(Vector3 position, Vector3 drift, string text, Color color, float scale)
    {
        Text = text;
        _color = color;
        _scale = Mathf.Clamp(scale <= 0f ? 1f : scale, 0.5f, 2.5f);

        bool emphatic = _scale >= DamagePopup.EmphasisScale;
        _life = emphatic ? EmphasisLifetime : Lifetime;
        _age = 0f;

        // Emphatic popups get a heavier outline as well as a bigger body. Size
        // alone is not enough: a large number over a crowded screen still reads
        // as noise unless it is also separated from whatever is behind it.
        OutlineSize = emphatic ? 14 : 8;

        _velocity = drift;
        _velocity.Y = emphatic ? 2.5f : 2.1f;

        GlobalPosition = position;
        Apply(1f, 1.35f);
        Visible = true;
    }

    /// <summary>Advance one frame. Returns false once the label is spent and
    /// should go back to the pool.</summary>
    public bool Step(float dt)
    {
        _age += dt;
        if (_age >= _life)
        {
            Visible = false;
            return false;
        }

        float t = _age / _life;

        // Rise and drift decay on the same constant, so the whole motion settles
        // together rather than sliding sideways after it stops climbing.
        _velocity *= Mathf.Exp(-Damping * dt);
        GlobalPosition += _velocity * dt;

        // Hold full opacity for the first half — a number that starts fading
        // immediately never gets a clean read — then fall away on a curve so the
        // tail is quick and the screen clears.
        float fade = t < 0.5f ? 1f : 1f - (t - 0.5f) * 2f;
        fade *= fade;

        // Pop in over the first eighth of the life, then hold. The overshoot is
        // small; a large one turns a screen full of numbers into a strobe.
        float pop = t < 0.125f ? Mathf.Lerp(1.35f, 1f, t / 0.125f) : 1f;

        Apply(fade, pop);
        return true;
    }

    private void Apply(float alpha, float pop)
    {
        PixelSize = BasePixelSize * _scale * pop;

        Color tint = _color;
        tint.A = alpha;
        Modulate = tint;

        Color outline = OutlineBase;
        outline.A = alpha;
        OutlineModulate = outline;
    }
}
