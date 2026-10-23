using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Visuals;

/// <summary>
/// --art-showcase=&lt;what&gt;: a staged scene that proves the art in one frame.
///
///   hero            every player look, plus the default look in each movement pose
///   enemies         every enemy in the catalog: ordinary row, elites, bosses
///   props-&lt;theme&gt;  a scattered patch of one <see cref="PropTheme"/>, from a
///                   gameplay-height camera, with a few characters for scale
///   closeup         the default hero and a handful of enemies, near
///
/// It is built from the same factories the game uses, so if a shot looks right
/// here it looks right in a run.
/// </summary>
public sealed partial class ShowcaseStage : Node3D
{
    private readonly string _what;
    private float _time;
    private readonly List<(CharacterRig rig, Vector3 velocity, RigPose pose)> _actors = new();
    private readonly List<CharacterRig> _flippers = new();
    private readonly List<CharacterRig> _strikers = new();

    public ShowcaseStage(string what)
    {
        _what = what;
        Name = "Showcase";
    }

    public override void _Ready()
    {
        string theme = _what.StartsWith("props-") ? _what["props-".Length..] : "meadow";
        PropTheme t = PropTheme.Get(theme);
        AddChild(PropFactory.MakeWorldEnvironment(t));
        AddChild(PropFactory.MakeSun(t));

        switch (_what)
        {
            case "hero": Hero(t); break;
            case "enemies": Enemies(t); break;
            case "closeup": Closeup(t); break;
            case string w when w.StartsWith("cute"): Cute(t, w); break;   // ShowcaseStage.Cute.cs
            default: Props(t); break;
        }

        if (Game.Screenshot.Requested) _ = Capture();
    }

    private async System.Threading.Tasks.Task Capture()
    {
        SceneTree tree = GetTree();
        await ToSignal(tree.CreateTimer(Game.Screenshot.AfterSeconds), SceneTreeTimer.SignalName.Timeout);
        await Game.Screenshot.Capture(tree, this, Game.Screenshot.Path!);
        GD.Print($"[showcase] {_what} done");
        tree.Quit();
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        foreach (var (rig, v, pose) in _actors)
        {
            rig.Velocity = v;
            rig.Pose = pose;
            rig.Grounded = pose is RigPose.Locomotion or RigPose.Slide;
        }
        // Loop the one-shot moves so a capture at any second catches them mid-way.
        int beat = (int)(_time / 1.3f);
        if (beat != _lastBeat)
        {
            _lastBeat = beat;
            foreach (CharacterRig r in _flippers) r.PlayFlip();
            foreach (CharacterRig r in _strikers) r.PlayAttackWindup(0.7f);
        }
    }

    private int _lastBeat = -1;

    // ---------------------------------------------------------------- scenes

    private void Floor(PropTheme t, float radius)
    {
        AddChild(PropFactory.BuildFloor(radius, t));
    }

    private Camera3D Camera(Vector3 at, Vector3 lookAt, float fov = 55f)
    {
        var cam = new Camera3D { Fov = fov, Current = true };
        AddChild(cam);
        cam.GlobalPosition = at;
        cam.LookAt(lookAt);
        return cam;
    }

    private CharacterRig Actor(ModelSpec spec, Vector3 at, bool faceZ, float yaw, Vector3 velocity, RigPose pose = RigPose.Locomotion)
    {
        CharacterRig rig = CharacterRig.Create(spec, faceZ, outline: true);
        var holder = new Node3D { Position = at, Rotation = new Vector3(0f, yaw, 0f) };
        AddChild(holder);
        holder.AddChild(rig);
        rig.ManualMotion = true;
        _actors.Add((rig, velocity, pose));
        return rig;
    }

    private void Label(string text, Vector3 at, float size = 0.6f)
    {
        AddChild(new Label3D
        {
            Text = text,
            Position = at,
            PixelSize = size * 0.01f,
            FontSize = 48,
            OutlineSize = 12,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color(1f, 1f, 1f),
            OutlineModulate = new Color(0f, 0f, 0f, 0.8f),
        });
    }

    private void Hero(PropTheme t)
    {
        Floor(t, 30f);
        // Front row: the imported looks; middle row: the procedural ones. Idle
        // and running alternately, turned 3/4 to camera.
        var imported = PlayerLooks.Ids.Where(i => !i.StartsWith("proc-")).Take(8).ToArray();
        var procedural = PlayerLooks.Ids.Where(i => i.StartsWith("proc-")).ToArray();
        void LookRow(string[] ids, float z)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                float x = (i - (ids.Length - 1) * 0.5f) * (ids.Length > 6 ? 2.6f : 2.3f);
                bool running = i % 2 == 1;
                Actor(PlayerLooks.Get(ids[i]), new Vector3(x, 0f, z), false, Mathf.Pi + 0.5f,
                      running ? new Vector3(0f, 0f, 5.5f) : Vector3.Zero);
                Label(ids[i], new Vector3(x, 2.3f, z));
            }
        }
        LookRow(imported, 3.6f);
        LookRow(procedural, 0.4f);
        // Back row: the default look in every movement pose, side-on.
        ModelSpec d = PlayerLooks.Get("default");
        (string name, RigPose pose, Vector3 v)[] poses =
        {
            ("run", RigPose.Locomotion, new Vector3(0f, 0f, 7f)),
            ("jump", RigPose.Air, new Vector3(0f, 3f, 5f)),
            ("flip", RigPose.Air, new Vector3(0f, 2f, 5f)),
            ("dash", RigPose.Dash, new Vector3(0f, 0f, 20f)),
            ("slide", RigPose.Slide, new Vector3(0f, 0f, 9f)),
            ("slam", RigPose.Slam, new Vector3(0f, -20f, 0f)),
        };
        for (int i = 0; i < poses.Length; i++)
        {
            float x = (i - (poses.Length - 1) * 0.5f) * 2.8f;
            float y = poses[i].pose is RigPose.Air or RigPose.Slam ? 0.5f : 0f;
            // Two rows: the imported default (clips + procedural layer) and the
            // procedural default (fully procedural poses).
            for (int row = 0; row < 2; row++)
            {
                ModelSpec spec = row == 0 ? d : PlayerLooks.Get("proc-default");
                float z = row == 0 ? -2.6f : -5.4f;
                CharacterRig r = Actor(spec, new Vector3(x, y, z), false, -Mathf.Pi * 0.5f, poses[i].v, poses[i].pose);
                if (poses[i].name == "flip") _flippers.Add(r);
            }
            Label(poses[i].name, new Vector3(x, 3.0f, -4f));
        }
        Camera(new Vector3(0f, 7.5f, 17f), new Vector3(0f, 0.4f, -0.5f), 50f);
    }

    private void Enemies(PropTheme t)
    {
        Floor(t, 40f);
        IReadOnlyList<EnemyDef> all = Catalog.Enemies;
        var normal = all.Where(e => !e.IsElite && !e.IsBoss).OrderBy(e => e.Height).ToList();
        var elites = all.Where(e => e.IsElite).OrderBy(e => e.Height).ToList();
        var bosses = all.Where(e => e.IsBoss).ToList();

        void Row(List<EnemyDef> defs, float z, float spacing, bool walking)
        {
            float x = -(defs.Sum(d => Mathf.Max(spacing, d.Height * 1.25f)) - spacing) * 0.5f;
            for (int i = 0; i < defs.Count; i++)
            {
                EnemyDef def = defs[i];
                float step = Mathf.Max(spacing, def.Height * 1.25f);
                var rig = CharacterRig.Create(EnemyVisuals.SpecFor(def), faceZ: true, outline: true);
                var holder = new Node3D { Position = new Vector3(x + step * 0.5f, 0f, z), Rotation = new Vector3(0f, -0.35f, 0f) };
                AddChild(holder);
                holder.AddChild(rig);
                rig.ManualMotion = true;
                bool walk = walking && i % 2 == 0;
                _actors.Add((rig, walk ? new Vector3(0f, 0f, def.MoveSpeed) : Vector3.Zero, RigPose.Locomotion));
                if (!walk) _strikers.Add(rig);
                Label(def.DisplayName, new Vector3(x + step * 0.5f, def.Height + 0.55f, z), 0.38f);
                x += step;
            }
        }
        Row(normal, 4f, 1.9f, true);
        Row(elites, -1.5f, 3.2f, true);
        Row(bosses, -8f, 6.5f, false);
        Camera(new Vector3(0f, 6f, 22f), new Vector3(0f, 1.4f, -1.5f), 46f);
    }

    private void Closeup(PropTheme t)
    {
        Floor(t, 20f);
        Actor(PlayerLooks.Get("default"), new Vector3(0f, 0f, 0f), false, Mathf.Pi + 0.4f, Vector3.Zero);
        string[] ids = { "husk", "mote", "gravel_brute", "skitterling" };
        for (int i = 0; i < ids.Length; i++)
        {
            EnemyDef? def = Catalog.Enemies.FirstOrDefault(e => e.Id == ids[i]);
            if (def is null) continue;
            var rig = CharacterRig.Create(EnemyVisuals.SpecFor(def), faceZ: true);
            var holder = new Node3D { Position = new Vector3(-3f + i * 2f, 0f, -2.5f), Rotation = new Vector3(0f, 0.3f, 0f) };
            AddChild(holder);
            holder.AddChild(rig);
            rig.ManualMotion = true;
            _actors.Add((rig, Vector3.Zero, RigPose.Locomotion));
        }
        Camera(new Vector3(0f, 2.2f, 5f), new Vector3(0f, 0.9f, -1f), 50f);
    }

    private void Props(PropTheme t)
    {
        const float radius = 40f;
        Floor(t, radius);
        var rng = new Core.Rng(12345);
        AddChild(PropFactory.Scatter(rng, radius, t, density: 1f, clearRadius: 4f));

        // Hero and a small crowd for scale, then the gameplay camera: 9.5 m back
        // on a -26 degree arm, the angle every run is seen from.
        Actor(PlayerLooks.Get("default"), Vector3.Zero, false, Mathf.Pi, new Vector3(0f, 0f, 0f));
        var defs = Catalog.Enemies.Where(e => !e.IsBoss && !e.IsElite).ToList();
        for (int i = 0; i < 14; i++)
        {
            EnemyDef def = defs[i % defs.Count];
            float a = -Mathf.Pi * 0.5f + (i - 7) * 0.16f;
            float r = 6f + (i % 4) * 2.2f;
            var rig = CharacterRig.Create(EnemyVisuals.SpecFor(def), faceZ: true);
            var pos = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            var holder = new Node3D { Position = pos };
            AddChild(holder);
            holder.LookAt(Vector3.Zero with { Y = 0f } + pos * 2f, Vector3.Up); // -Z away from hero => +Z toward
            holder.AddChild(rig);
            rig.ManualMotion = true;
            _actors.Add((rig, new Vector3(0f, 0f, def.MoveSpeed), RigPose.Locomotion));
        }
        float pitch = Mathf.DegToRad(26f);
        Vector3 pivot = new(0f, 1.6f, 0f);
        Camera(pivot + new Vector3(0f, Mathf.Sin(pitch), Mathf.Cos(pitch)) * 9.5f, pivot, 75f);
    }
}
