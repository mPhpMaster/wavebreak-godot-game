using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Audio;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

/// <summary>
/// --check-currents (with --route=reef-dive): water currents end to end.
/// Forced currents at the swimmer must carry it along a horizontal, an up and
/// a down flow, not push during the telegraph, speed it up when it swims with
/// the flow, let it swim out sideways, and vanish on time. The seeded schedule
/// is then walked for fifteen minutes: lifetimes in range, every current in open
/// water (re-tested independently), both kinds present, and the same cycles
/// whether the clock is walked or jumped (a co-op late joiner) or rebuilt.
/// --currents-shot=&lt;prefix&gt; also saves &lt;prefix&gt;-horizontal.png and -vertical.png.
/// Prints CURRENTS PASS / FAIL.
/// </summary>
public sealed partial class CurrentsCheck : Node
{
    private readonly Player _player;
    private ReefCurrentField _field = null!;

    public CurrentsCheck(Player player) => _player = player;
    public override void _Ready() => _ = Run();

    private async Task Wait(float seconds) => await ToSignal(GetTree().CreateTimer(seconds, true, true), SceneTreeTimer.SignalName.Timeout);

    /// <summary>Per-item checks: silent when they hold, so the log stays readable.</summary>
    private static void Quiet(bool value, string label) { if (!value) Require(false, label); }

    private static void Require(bool value, string label)
    {
        GD.Print($"CURRENTS {(value ? "OK" : "FAIL")} {label}");
        if (!value) throw new InvalidOperationException(label);
    }

    private async Task Run()
    {
        try
        {
            await Wait(0.8f);
            Require(Arena.Current.Underwater && ReefCurrentField.Instance is not null, "the underwater map has a current field");
            _field = ReefCurrentField.Instance!;
            _field.RandomEnabled = false;
            EnemyManager enemies = EnemyManager.Instance!;
            enemies.ClearField(999); enemies.Quiet = true; EnemyManager.DevFrozen = true;
            _player.MoveOverride = static () => Vector3.Zero;

            Lifecycle();
            await Horizontal();
            await Vertical(true);
            await Vertical(false);
            await Schedule();
            foreach (string arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--currents-shot=")) await Shots(arg["--currents-shot=".Length..]);
            GD.Print("CURRENTS PASS");
            GetTree().Quit();
        }
        catch (Exception error) { GD.Print("CURRENTS FAIL " + error.Message); GetTree().Quit(1); }
    }

    private static void Lifecycle()
    {
        var flow = new ReefCurrentFlow { From = Vector3.Zero, To = Vector3.Right * 20, Radius = 4, Strength = 7, Born = 10, ActiveSeconds = 10 };
        Require(flow.Push(10.5f) == 0f && flow.Visibility(10.5f) > 0f, "telegraph: visible, no push");
        Require(Mathf.IsEqualApprox(flow.Push(flow.PushStart + 1f), 1f) && Mathf.IsEqualApprox(flow.Push(flow.FadeStart + 1f), 0.5f)
            && flow.Push(flow.Dies) == 0f && flow.Visibility(flow.Dies) == 0f && !flow.Alive(flow.Dies), "push ramps in, holds, fades out, then the current is gone");
        Require(Mathf.IsEqualApprox(flow.PushStart - flow.Born, 1.5f), "the telegraph lasts 1.5 s");
        Require(flow.Weight(new Vector3(10, 0, 0)) > 0.99f && flow.Weight(new Vector3(10, 4.1f, 0)) == 0f && flow.Weight(new Vector3(-3, 0, 0)) == 0f,
            "full in the core, nothing outside the capsule or past its ends");
    }

    /// <summary>Open water for a 40 m flow along <paramref name="direction"/> starting
    /// six metres behind the returned point.</summary>
    private Vector3 FindStart(Vector3 direction, float radius)
    {
        var tries = new List<Vector3>();
        for (int ring = 0; ring < 6; ring++)
            for (int k = 0; k < 8; k++)
            {
                float angle = k * Mathf.Tau / 8f, distance = ring * 14f;
                tries.Add(new Vector3(Mathf.Cos(angle) * distance, 0, Mathf.Sin(angle) * distance));
            }
        foreach (Vector3 flat in tries)
            for (float lift = 6f; lift <= 50f; lift += 4f)
            {
                Vector3 at = flat with { Y = ReefCavePath.Floor(flat) + radius + lift };
                var probe = new ReefCurrentFlow { From = at - direction * 6f, To = at + direction * 34f, Radius = radius, Strength = 7 };
                if (ReefCurrentField.OpenWater(_field, probe, out _)) return at;
            }
        throw new InvalidOperationException("no open water for a test current along " + direction);
    }

    /// <summary>Teleport to a fresh forced current's start, sit through its
    /// telegraph, then steer with <paramref name="steer"/> for <paramref name="seconds"/>.</summary>
    private async Task<(Vector3 Telegraph, Vector3 Ride, ReefCurrentFlow Flow)> Ride(Vector3 direction, ReefCurrentKind kind, Func<Vector3> steer, float seconds)
    {
        const float radius = 5f;
        Vector3 at = FindStart(direction, radius);
        _player.MoveOverride = static () => Vector3.Zero;
        _player.Teleport(at - Vector3.Up * (Player.BodyHeight * 0.5f));
        await Wait(0.1f);
        ReefCurrentFlow flow = _field.Force(at - direction * 6f, at + direction * 34f, radius, 7f, seconds + 0.5f, kind);
        Vector3 before = _player.CenterPosition;
        await Wait(1.2f);
        Vector3 telegraph = _player.CenterPosition - before;
        while (_field.Clock < flow.PushStart) await Wait(0.05f);
        _player.MoveOverride = steer;
        Vector3 start = _player.CenterPosition;
        await Wait(seconds);
        Vector3 ride = _player.CenterPosition - start;
        _player.MoveOverride = static () => Vector3.Zero;
        return (telegraph, ride, flow);
    }

    private async Task Horizontal()
    {
        Vector3 dir = new Vector3(1, 0, 0.35f).Normalized();
        Vector3 across = new(-dir.Z, 0, dir.X);
        var idle = await Ride(dir, ReefCurrentKind.Horizontal, static () => Vector3.Zero, 2f);
        Require(idle.Telegraph.Length() < 0.4f, $"no push while it telegraphs (moved {idle.Telegraph.Length():0.00} m)");
        Require(_field.Live.Contains(idle.Flow) && ReefCheckHelpers.Find<ReefCurrentStreams>(_field)!.StreaksDrawn > 0, "a live current is drawn");
        float along = idle.Ride.Dot(dir);
        Require(along > 7f && Mathf.Abs(idle.Ride.Dot(across)) < 1.2f && Mathf.Abs(idle.Ride.Y) < 1.5f,
            $"horizontal: an idle swimmer is carried along the flow ({along:0.0} m in 2 s, off-axis {idle.Ride.Dot(across):0.0}/{idle.Ride.Y:0.0})");
        Require(ReefCheckHelpers.Find<ReefCurrentAudio>(_field)!.Level > 0.5f, "the current's rushing loop plays while it pushes");
        Require(_player.InReefCurrent, "the swimmer knows it is in a current (hint text)");
        // Gone on time: the fade ends at Dies and nothing is left behind.
        while (_field.Clock < idle.Flow.Dies + 0.1f) await Wait(0.1f);
        await Wait(0.1f);
        Require(!_field.Live.Contains(idle.Flow) && ReefCurrentField.VelocityAt(idle.Flow.Centre) == Vector3.Zero,
            "the current disappears when its fade ends");

        var with = await Ride(dir, ReefCurrentKind.Horizontal, () => dir, 2f);
        Require(with.Ride.Dot(dir) > along + 6f, $"swimming with the flow is faster ({with.Ride.Dot(dir):0.0} m vs {along:0.0} m idle)");
        _field.ClearForced();

        var against = await Ride(dir, ReefCurrentKind.Horizontal, () => -dir, 2f);
        Require(Mathf.Abs(against.Ride.Dot(dir)) < along * 0.5f, $"swimming against it nearly holds station ({against.Ride.Dot(dir):0.0} m)");
        _field.ClearForced();

        var cross = await Ride(dir, ReefCurrentKind.Horizontal, () => across, 2.5f);
        Vector3 at = _player.CenterPosition;
        Vector3 line = cross.Flow.To - cross.Flow.From;
        float off = at.DistanceTo(cross.Flow.From + line * Mathf.Clamp((at - cross.Flow.From).Dot(line) / line.LengthSquared(), 0, 1));
        Require(off > cross.Flow.Radius && ReefCurrentField.VelocityAt(at) == Vector3.Zero,
            $"swimming sideways leaves the flow ({off:0.0} m off the axis after 2.5 s)");
        _field.ClearForced();
    }

    private async Task Vertical(bool up)
    {
        Vector3 dir = up ? Vector3.Up : Vector3.Down;
        var idle = await Ride(dir, up ? ReefCurrentKind.Updraft : ReefCurrentKind.Downdraft, static () => Vector3.Zero, 2f);
        Require(idle.Telegraph.Length() < 0.4f, $"{(up ? "updraft" : "downdraft")}: no push while it telegraphs");
        float along = idle.Ride.Dot(dir);
        Require(along > 6f && new Vector2(idle.Ride.X, idle.Ride.Z).Length() < 1.2f,
            $"{(up ? "updraft" : "downdraft")}: an idle swimmer is carried {(up ? "up" : "down")} ({along:0.0} m in 2 s)");
        _field.ClearForced();
    }

    private async Task Schedule()
    {
        const float horizon = 900f;
        // Walk the clock like a player who stayed the whole phase...
        for (float t = 0f; t <= horizon; t += 0.5f)
            for (int slot = 0; slot < ReefCurrentField.Slots; slot++) _field.CurrentOf(slot, t);
        // ...and jump straight to the end like a late joiner.
        var jumped = new ReefCurrentField(null);
        var other = new ReefCurrentField(0xC0FFEEUL);
        _field.GetParent().AddChild(jumped); _field.GetParent().AddChild(other);
        await Wait(0.1f);
        jumped.CurrentOf(0, horizon); other.CurrentOf(0, horizon);
        bool same = true, different = false;
        int flows = 0, empty = 0, vertical = 0, level = 0, peak = 0;
        var space = _field.GetWorld3D().DirectSpaceState;
        float lowestWave = WaterSurfaceHeight.BaseHeight - 2.4f;
        for (int slot = 0; slot < ReefCurrentField.Slots; slot++)
        {
            IReadOnlyList<ReefCurrentFlow> a = _field.Cycles(slot), b = jumped.Cycles(slot), c = other.Cycles(slot);
            int n = Math.Min(a.Count, b.Count);
            Require(n > 10, $"slot {slot} cycles through many currents in fifteen minutes ({a.Count})");
            for (int i = 0; i < n; i++)
                same &= a[i].Born == b[i].Born && a[i].From == b[i].From && a[i].To == b[i].To && a[i].Strength == b[i].Strength;
            for (int i = 0; i < Math.Min(n, c.Count); i++) different |= a[i].From != c[i].From;
            for (int i = 0; i < a.Count; i++)
            {
                ReefCurrentFlow f = a[i];
                if (i > 0) Quiet(f.Born >= a[i - 1].Dies + ReefCurrentField.MinGap - 0.01f, $"slot {slot} rests between currents");
                Quiet(f.ActiveSeconds >= ReefCurrentField.MinActive && f.ActiveSeconds <= ReefCurrentField.MaxActive, $"active time {f.ActiveSeconds:0.0} s in range");
                if (f.Empty) { empty++; continue; }
                flows++;
                if (f.Vertical) vertical++; else level++;
                Quiet(f.Length >= ReefCurrentField.MinLength - 0.01f && f.Length <= ReefCurrentField.MaxLength + 0.01f, $"length {f.Length:0.0} m in range");
                Quiet(ReefCurrentField.OpenWater(_field, f, out string why), $"slot {slot} cycle {i} is in open water {why}");
                // Independent of the placement test: terrain under, waves over, nothing solid on the axis.
                for (int k = 0; k <= 10; k++)
                {
                    Vector3 p = f.From.Lerp(f.To, k / 10f);
                    Quiet(p.Y - f.Radius > UnderwaterTerrain.Height(p) && p.Y - f.Radius > ReefCavePath.Floor(p) && p.Y + f.Radius < lowestWave,
                        $"slot {slot} cycle {i} stays between seabed and surface at {p}");
                }
                var ray = PhysicsRayQueryParameters3D.Create(f.From, f.To, 1u);
                ray.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
                var hit = space.IntersectRay(ray);
                Quiet(hit.Count == 0 || hit["collider"].AsGodotObject() is not StaticBody3D body
                    || ((string)body.Name is not ("Solids" or "ExplorationCollision") && !body.Name.ToString().StartsWith("CliffLedge")),
                    $"slot {slot} cycle {i} does not pass through rock");
            }
        }
        for (float t = 0f; t <= horizon; t += 1f)
        {
            int alive = 0;
            for (int slot = 0; slot < ReefCurrentField.Slots; slot++)
                if (_field.CurrentOf(slot, t) is { } f && f.Alive(t)) alive++;
            peak = Math.Max(peak, alive);
        }
        jumped.QueueFree(); other.QueueFree();
        GD.Print($"CURRENTS schedule flows={flows} empty={empty} level={level} vertical={vertical} peak={peak} tried={_field.PlacementsTried} rejected={_field.PlacementsRejected}");
        Require(same, "the same cycles whether the clock is walked or jumped (late join), and on a fresh field (every peer)");
        Require(different, "another run seed places the currents elsewhere");
        Require(empty * 4 < flows, "most cycles find open water");
        Require(level > 0 && vertical > 0, "both horizontal and vertical currents occur");
        Require(peak >= 3, $"several currents can be alive at once ({peak})");
    }

    private async Task Shots(string prefix)
    {
        PlayerCamera camera = PlayerCamera.Instance!;
        // Horizontal: watch it run across the view from the side.
        Vector3 dir = new Vector3(1, 0, 0.2f).Normalized();
        Vector3 at = FindStart(dir, 5f);
        Vector3 side = new(-dir.Z, 0, dir.X);
        _field.Force(at - dir * 6f, at + dir * 34f, 5f, 7f, 6f, ReefCurrentKind.Horizontal);
        Vector3 eye = at + dir * 13f + side * 20f + Vector3.Up * 1.5f;
        _player.Teleport(eye - Vector3.Up * Player.BodyHeight * 0.5f);
        camera.FaceDirection(-side); camera.SetPitchDegrees(-5); camera.SnapToTarget();
        await Wait(2.6f);
        await Screenshot.Capture(GetTree(), this, prefix + "-horizontal.png");
        _field.ClearForced();
        // Vertical: an updraft column seen from a little way off and below its middle.
        at = FindStart(Vector3.Up, 4f);
        _field.Force(at - Vector3.Up * 6f, at + Vector3.Up * 34f, 4f, 7f, 6f, ReefCurrentKind.Updraft);
        Vector3 back = new Vector3(0.3f, 0, 1f).Normalized();
        eye = at + back * 26f - Vector3.Up * 3f;
        _player.Teleport(eye - Vector3.Up * Player.BodyHeight * 0.5f);
        camera.FaceDirection(-back); camera.SetPitchDegrees(18); camera.SnapToTarget();
        await Wait(2.6f);
        await Screenshot.Capture(GetTree(), this, prefix + "-vertical.png");
        _field.ClearForced();
    }
}
