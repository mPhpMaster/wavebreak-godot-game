using Godot;
using Wavebreak.Combat;
using Wavebreak.Meta;
using Wavebreak.Visuals;

namespace Wavebreak.Net;

/// <summary>
/// Another player as this machine sees them: their character and skin, their
/// name over their head, and a body that follows the transform they send.
///
/// Purely a puppet. It has no collision (players never block each other), no
/// weapons and no health of its own: each player's health lives on the
/// machine that plays them. On the host it is also what enemies chase and
/// swing at; a swing is forwarded to its owner (<see cref="TakeDamage"/>), who
/// runs it through their own armour, dodge and i-frames.
/// </summary>
public sealed partial class RemotePlayer : Node3D, IDamageable
{
    public long PeerId { get; }
    public int Slot { get; }
    public string DisplayName { get; private set; }

    /// <summary>Downed and waiting for a revive.</summary>
    public bool Down { get; private set; }
    public float Health01 { get; private set; } = 1f;
    public float PickupRange { get; private set; } = 3f;
    /// <summary>Their own revive progress while down, 0..1.</summary>
    public float Revive01 { get; private set; }
    /// <summary>Which way their camera faces (the automated test huddles on it).</summary>
    public float CameraYaw { get; private set; }
    public bool HasState { get; private set; }

    private Node3D _yawNode = null!;
    private CharacterRig _rig = null!;
    private Label3D _tag = null!;
    private MeshInstance3D _reviveRing = null!;
    private StandardMaterial3D _reviveMat = null!;

    private Vector3 _netPos;
    private Vector3 _netVel;
    private float _netYaw;
    private double _netAt;
    private RigPose _pose = RigPose.Locomotion;
    private bool _grounded = true;
    private float _tilt;

    public RemotePlayer(CoopPeer peer)
    {
        PeerId = peer.Id;
        Slot = peer.Slot;
        DisplayName = peer.Name;
        Name = $"Remote_{peer.Id}";
        _look = peer;
    }

    private readonly CoopPeer _look;

    public Vector3 CenterPosition => GlobalPosition + Vector3.Up * (Actors.Player.BodyHeight * 0.5f);

    public override void _Ready()
    {
        Color color = Coop.SlotColor(Slot);
        _yawNode = new Node3D { Name = "Yaw" };
        AddChild(_yawNode);

        CharacterDef c = Characters.ById(_look.CharacterId) ?? Characters.Default;
        SkinDef skin = c.Skins[0];
        foreach (SkinDef s in c.Skins) if (s.Id == _look.SkinId) skin = s;
        _rig = CharacterRig.Create(PlayerLooks.Get(skin.LookId));
        _rig.ManualMotion = true;
        _yawNode.AddChild(_rig);
        _rig.EnableShadow();
        _rig.SetRecolor(skin.Paint);
        if (_look.HatId.Length > 0) _rig.SetHat(_look.HatId);

        // A ring in the player's colour at their feet: who is who at a glance,
        // even when two people picked the same character and skin.
        var ringMat = new StandardMaterial3D
        {
            AlbedoColor = color with { A = 0.55f },
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        AddChild(new MeshInstance3D
        {
            Name = "SlotRing",
            Mesh = new TorusMesh { InnerRadius = 0.62f, OuterRadius = 0.78f, Rings = 24, RingSegments = 4 },
            MaterialOverride = ringMat,
            Position = new Vector3(0f, 0.05f, 0f),
            Scale = new Vector3(1f, 0.1f, 1f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        _reviveMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.45f, 1f, 0.55f, 0.65f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _reviveRing = new MeshInstance3D
        {
            Name = "ReviveRing",
            Mesh = new TorusMesh { InnerRadius = 0.92f, OuterRadius = 1f, Rings = 40, RingSegments = 4 },
            MaterialOverride = _reviveMat,
            Position = new Vector3(0f, 0.08f, 0f),
            Scale = new Vector3(Coop.ReviveRadius, 0.1f, Coop.ReviveRadius),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_reviveRing);

        _tag = new Label3D
        {
            Name = "NameTag",
            Text = DisplayName,
            Modulate = color,
            OutlineModulate = new Color(0.02f, 0.02f, 0.04f),
            OutlineSize = 12,
            FontSize = 64,
            PixelSize = 0.008f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Shaded = false,
            RenderPriority = 4,
            OutlineRenderPriority = 3,
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,   // a name is a name
            Position = new Vector3(0f, Actors.Player.BodyHeight + 0.75f, 0f),
        };
        AddChild(_tag);
    }

    /// <summary>A state packet from the owner.</summary>
    public void ApplyState(Vector3 pos, float yaw, Vector3 vel, int flags, float health, float range, float revive, float cameraYaw)
    {
        if (!HasState) GlobalPosition = pos;
        HasState = true;
        _netPos = pos;
        _netVel = vel;
        _netYaw = yaw;
        _netAt = Time.GetTicksMsec() / 1000.0;
        _pose = (RigPose)(flags & 7);
        _grounded = (flags & 8) != 0;
        bool down = (flags & 16) != 0;
        Health01 = Mathf.Clamp(health, 0f, 1f);
        PickupRange = Mathf.Max(0.5f, range);
        Revive01 = Mathf.Clamp(revive, 0f, 1f);
        CameraYaw = cameraYaw;
        Down = down;
    }

    /// <summary>Snap to a point (a new map); the next state packet confirms it.</summary>
    public void Place(Vector3 at)
    {
        GlobalPosition = at;
        _netPos = at;
        _netVel = Vector3.Zero;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (HasState)
        {
            // Dead reckoning on the last velocity, at most a fifth of a second
            // ahead, then a smooth pull toward it: packets come at 20 Hz.
            float ahead = Mathf.Min(0.2f, (float)(Time.GetTicksMsec() / 1000.0 - _netAt));
            Vector3 target = _netPos + _netVel * ahead;
            if (GlobalPosition.DistanceSquaredTo(target) > 64f) GlobalPosition = target;
            else GlobalPosition = GlobalPosition.Lerp(target, 1f - Mathf.Exp(-14f * dt));
        }

        float yaw = Mathf.LerpAngle(_yawNode.Rotation.Y, _netYaw, 1f - Mathf.Exp(-14f * dt));
        _tilt = Mathf.MoveToward(_tilt, Down ? 1f : 0f, dt * 2.2f);
        _yawNode.Rotation = new Vector3(-_tilt * 1.45f, yaw, 0f);

        _rig.Velocity = Down ? Vector3.Zero : _netVel;
        _rig.Grounded = _grounded;
        _rig.Pose = Down ? RigPose.Locomotion : _pose;

        _reviveRing.Visible = Down;
        if (Down)
        {
            float pulse = 0.35f + 0.3f * Revive01 + 0.1f * Mathf.Sin((float)Time.GetTicksMsec() * 0.008f);
            _reviveMat.AlbedoColor = new Color(0.45f, 1f, 0.55f, pulse);
        }
        _tag.Text = Down ? Loc.F("{0} (down)", DisplayName) : DisplayName;
    }

    // ------------------------------------------------------------ IDamageable

    /// <summary>On the host, an enemy hit this player: forward it to them.</summary>
    public void TakeDamage(DamageInfo info)
    {
        if (info is null) return;
        if (Down || !Coop.IsHost)
        {
            info.Nullified = true;
            info.Amount = 0f;
            return;
        }
        NetSession.Instance?.HurtPeer(PeerId, info);
    }

    public bool IsAlive => !Down && HasState && IsInsideTree();

    Vector3 IDamageable.Position => CenterPosition;
}
