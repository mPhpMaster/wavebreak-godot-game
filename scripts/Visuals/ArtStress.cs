using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;

namespace Wavebreak.Visuals;

/// <summary>
/// --art-stress=N: keeps N extra enemies alive around the player for the whole
/// capture and prints one STRESS line with the measured frame times.
///
/// It exists because "hundreds of enemies" is the budget the art has to fit in,
/// and the director only reaches that crowd around minute ten — far too late for
/// a quick before/after comparison. These enemies are real <see cref="Enemy"/>
/// nodes (same body, same rig, same per-frame step) but are not registered with
/// the manager, so weapons ignore them and the count stays constant: the number
/// measured is the cost of drawing and animating a crowd, not of killing one.
///
/// Frame times are wall-clock (Time.GetTicksUsec) rather than the process delta:
/// under --fixed-fps the delta is a constant 1/60 and would report a perfect
/// 60 fps whatever the machine was actually doing.
/// </summary>
public sealed partial class ArtStress : Node
{
    private const float SpawnAt = 1.0f;     // let the scene settle first
    private const float MeasureFrom = 2.5f; // and the first animation frames warm up

    private readonly int _count;
    private readonly float _stopAt;
    private readonly List<Enemy> _crowd = new();
    private readonly List<float> _ring = new();
    private readonly float[] _frames = new float[20000];
    private int _frameCount;
    private ulong _lastTicks;
    private float _time;
    private bool _reported;
    private long _drawCalls;
    private int _drawSamples;

    public ArtStress(int count, float stopAt)
    {
        _count = count;
        _stopAt = stopAt;
        Name = "ArtStress";
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        ulong now = Time.GetTicksUsec();
        if (_time >= MeasureFrom && _lastTicks != 0 && !_reported && _frameCount < _frames.Length)
        {
            _frames[_frameCount++] = (now - _lastTicks) / 1000f;
            _drawCalls += (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
            _drawSamples++;
        }
        _lastTicks = now;

        if (_crowd.Count == 0 && _time >= SpawnAt) SpawnCrowd();
        if (!_reported && _time >= _stopAt) Report();
    }

    private void SpawnCrowd()
    {
        if (GetParent() is not Node3D world || Game.Main.Instance?.Player is not { } player) return;

        // Every ordinary kind in rotation, plus a few elites: the crowd a late
        // run actually fields, rather than 150 copies of the cheapest mesh.
        var defs = new List<EnemyDef>();
        foreach (EnemyDef d in Catalog.Enemies) if (!d.IsBoss) defs.Add(d);

        var rng = new RandomNumberGenerator { Seed = 777 };
        for (int i = 0; i < _count; i++)
        {
            EnemyDef def = defs[i % defs.Count];
            if (def.IsElite && i % 25 != 0) def = defs[0];
            var e = new Enemy { Name = $"Stress{i}" };
            world.AddChild(e);
            e.Configure(def, 1f, 0f, 1f); // zero damage: the player's death is not the subject
            float ring = rng.RandfRange(3.5f, 24f);
            float a = rng.RandfRange(0f, Mathf.Tau);
            e.Spawn(player.GlobalPosition + new Vector3(Mathf.Cos(a) * ring, 0f, Mathf.Sin(a) * ring));
            _crowd.Add(e);
            _ring.Add(ring);
        }
        GD.Print($"[stress] spawned {_crowd.Count} enemies");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_crowd.Count == 0 || Game.Main.Instance?.Player is not { } player) return;
        float dt = (float)delta;
        Vector3 p = player.GlobalPosition;

        // Each enemy circles the player on its own ring, so the whole crowd keeps
        // walking (the animation cost is the thing being measured) and spreads
        // across the screen the way a real swarm does, instead of collapsing onto
        // one point that would make the frame look cheaper than it is.
        for (int i = 0; i < _crowd.Count; i++)
        {
            Enemy e = _crowd[i];
            Vector3 to = p - e.GlobalPosition;
            to.Y = 0f;
            float d = Mathf.Max(0.01f, to.Length());
            Vector3 heading = to / d;
            Vector3 tangent = new(-heading.Z, 0f, heading.X);
            float radial = Mathf.Clamp((d - _ring[i]) * 0.6f, -1f, 1f);
            Vector3 sep = tangent * 0.9f - heading * (1f - radial) * 0.75f;
            // The bot outruns most of them; a crowd left behind off-screen
            // would measure an empty frame. Tether stragglers back to their ring.
            if (d > _ring[i] + 8f) e.GlobalPosition = new Vector3(p.X, e.GlobalPosition.Y, p.Z) - heading * _ring[i];
            e.Step(dt, p, null, sep);
        }
    }

    private void Report()
    {
        _reported = true;
        if (_frameCount == 0) { GD.Print("STRESS no frames measured"); return; }

        var sorted = new float[_frameCount];
        System.Array.Copy(_frames, sorted, _frameCount);
        System.Array.Sort(sorted);
        double sum = 0;
        foreach (float f in sorted) sum += f;
        float avg = (float)(sum / _frameCount);
        float p95 = sorted[(int)(_frameCount * 0.95f)];
        float p99 = sorted[(int)(_frameCount * 0.99f)];
        long draws = _drawSamples > 0 ? _drawCalls / _drawSamples : 0;
        int alive = Game.Main.Instance?.Enemies.AliveCount ?? 0;
        GD.Print(System.FormattableString.Invariant(
            $"STRESS crowd={_crowd.Count} director={alive} frames={_frameCount} avg_ms={avg:0.00} fps={1000f / avg:0.0} p95_ms={p95:0.00} p99_ms={p99:0.00} draws={draws}"));
    }
}
