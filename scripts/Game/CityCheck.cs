using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Meta;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

/// <summary>Opt-in checks against the actual city collision and controller.</summary>
public sealed partial class CityCheck : Node
{
    private readonly Player _player;
    private bool _passed = true;
    private readonly string? _capture;
    public CityCheck(Player player, string? capture) { _player = player; _capture = capture; }
    public override void _Ready()
    {
        if (_capture is not null) Engine.MaxFps = 5;
        _ = Run();
    }
    private async Task Wait(float seconds) => await ToSignal(GetTree().CreateTimer(seconds, true, true), SceneTreeTimer.SignalName.Timeout);
    private void Check(bool condition, string label)
    { _passed &= condition; GD.Print($"CITY {(condition ? "OK" : "FAIL")} {label}"); }

    private async Task Run()
    {
        try
        {
            await Wait(0.7f);
            _player.MoveOverride = static () => Vector3.Zero;
            EnemyManager manager = EnemyManager.Instance!;
            manager.Quiet = true; manager.ClearField(999f);
            Check(Arena.IsCity, "optional city is the active first map");
            Check(ReefCheckHelpers.Find<Arena>(GetTree().Root)?.CityBuildingCount >= 48, "dense city has at least 48 solid towers");
            Check(CityRoofCheck.Matches(ReefCheckHelpers.Find<Arena>(GetTree().Root)!), "rendered wall and roof meet without a gap and match the physical roof");
            Check(Stages.All.Count == 6 && !Stages.All.Contains(Stages.Bellspire), "standard map draw pool unchanged");
            foreach (string id in new[] { "nori", "vesper" })
            {
                CharacterDef hero = Characters.ById(id)!;
                Check(hero.IsAvailable && hero.UnlockedBy is null && hero.Skins[0].IsAvailable, id + " and base skin open without a migration");
                Check(PlayerLooks.Get(hero.LookId).Custom is not null && PlayerLooks.Get(hero.LookId).Jointed, id + " has an original jointed model");
            }
            _player.Teleport(Vector3.Up * 0.1f);
            await Wait(0.15f);
            Vector3 center = _player.CenterPosition;
            bool surface = GrappleAnchors.Ray(_player, center, (new Vector3(18f, 17f, -16f) - center).Normalized(), center, out Vector3 anchor);
            Check(surface, "first visible tower surface accepts a hook");
            Vector3 sight = (anchor - center).Normalized();
            Check(GrappleAnchors.Ray(_player, center - sight * 0.7f, sight, center, out _),
                "third-person aim ray ignores the player capsule");
            Check(!GrappleAnchors.Ray(_player, center + Vector3.Up * 2f, Vector3.Down, center, out _), "ground never accepts hooks");
            Check(!GrappleAnchors.Valid(_player, center, new Vector3(-18f, 9f, -28.12f)), "opposite wall cannot be hooked through a building");
            Check(!GrappleAnchors.Valid(_player, center, new Vector3(130f, 45f, 130f)), "out-of-range anchor rejected");
            Input.ActionPress("city_hook_left");
            Check(_player.AttachHook(anchor), "controller attaches to validated structure");
            await Wait(0.9f);
            Check(_player.Grappling && _player.GlobalPosition.Y > 1f, "reeling raises the player with a real tether");
            GrappleTether cable = _player.GetNode<GrappleTether>("GrappleCable");
            Vector3 cableTip = cable.GlobalPosition + cable.GlobalBasis.Y * 0.5f;
            Vector3 cableBase = cable.GlobalPosition - cable.GlobalBasis.Y * 0.5f;
            Check(cableTip.DistanceTo(_player.HookAnchor) < 0.1f && cableBase.DistanceTo(_player.HookOrigin(HookSide.Left)) < 0.1f,
                "visible cable endpoints match the player and physical anchor");
            Vector3 attachedVelocity = _player.Velocity;
            GD.Print($"CITY tether position={_player.GlobalPosition} velocity={attachedVelocity}");
            Input.ActionRelease("city_hook_left");
            await Wait(0.08f);
            Check(!_player.Grappling && _player.Velocity.Length() > 1f, "release retains airborne momentum");
            Check(_player.Velocity.Y < attachedVelocity.Y, "released flight follows gravity");

            _player.Teleport(Vector3.Up * 0.2f); await Wait(0.12f);
            Input.ActionPress("city_hook_left"); _player.AttachHook(anchor); await Wait(0.2f);
            _player.RequestJump(); await Wait(0.05f);
            Check(_player.Grappling && _player.HookBoosts > 0 && _player.Velocity.Y > 0f, "jump boosts while retaining its structural tether");
            await Wait(0.15f);
            int boosts = _player.HookBoosts;
            _player.RequestJump(); await Wait(.04f);
            Check(_player.Grappling && _player.HookBoosts == boosts, "attached boost cooldown rejects repeated jump spam");
            Input.ActionRelease("city_hook_left");
            _player.Teleport(new Vector3(-18f, 15f, -22f)); await Wait(1f);
            Check(_player.IsOnFloor() && Mathf.Abs(_player.GlobalPosition.Y - 13f) < 0.2f, "visible starter roof matches its collision height");

            manager.Quiet = false; EnemyManager.DevFrozen = true;
            Enemy giant = manager.SpawnAt(Catalog.EnemyById("city_bellwarden")!, new Vector3(0f, 0f, -48f))!;
            Check(giant is { IsCityGiant: true } && giant.Def!.Height / Player.BodyHeight > 14f, "warden towers over the player at full scale");
            giant!.Rotation = Vector3.Zero;
            Vector3 core = giant.ClosestCore(new Vector3(0f, 17f, -40f));
            _player.Teleport(core + Vector3.Back * 5f - Vector3.Up * 0.9f); await Wait(0.08f);
            _player.SetPhysicsProcess(false); _player.Velocity = Vector3.Right * 20f;
            float hp = giant.Health;
            int strikes = _player.CoreStrikes;
            Input.ActionPress("dash"); _player.SetPhysicsProcess(true); await Wait(0.04f);
            Input.ActionRelease("dash"); _player.SetPhysicsProcess(false);
            Check(_player.CoreStrikes > strikes && giant.Health < hp - 300f,
                "bound dash input delivers meaningful airborne 3D core damage");
            Check(_player.CoreStrikes == strikes + 1, "one dash press causes exactly one core strike");
            Check(!_player.TryCoreStrike(), "core strikes respect cooldown");
            _player.SetPhysicsProcess(true); await Wait(0.8f);
            _player.Teleport(new Vector3(0f, 0.2f, -40f)); await Wait(0.08f);
            _player.SetPhysicsProcess(false); _player.Velocity = Vector3.Right * 20f;
            Check(!_player.TryCoreStrike(), "standing at a giant's feet cannot strike its elevated core");

            giant.Spawn(new Vector3(0f, 0f, -48f));
            var target = new CityCheckTarget { Position = new Vector3(7f, 0f, -48f) };
            for (int i = 0; i < 310; i++) giant.Step(1f / 60f, target.Position, target, Vector3.Zero);
            Check(target.Hits == 1, "telegraphed ground stomp resolves exactly once");
            giant.Spawn(new Vector3(0f, 0f, -48f));
            target = new CityCheckTarget { Position = new Vector3(7f, 18f, -48f) };
            for (int i = 0; i < 190; i++) giant.Step(1f / 60f, target.Position, target, Vector3.Zero);
            Check(giant.CityAttackRemaining > 0f, "aerial sweep has a visible wind-up");
            target.Position += Vector3.Right * 16f;
            for (int i = 0; i < 120; i++) giant.Step(1f / 60f, target.Position, target, Vector3.Zero);
            Check(target.Hits == 0, "moving outside the committed 3D sweep avoids damage");
            giant.Spawn(new Vector3(0f, 0f, -64f));
            target = new CityCheckTarget { Position = new Vector3(64f, 0f, -48f) };
            var probe = new CityPathState();
            CityNavigation.Next(giant.GlobalPosition, target.Position, probe, 0.1f);
            GD.Print($"CITY path={string.Join(";", probe.Points)}");
            for (int i = 0; i < 2400; i++)
            {
                giant.Step(1f / 60f, target.Position, target, Vector3.Zero);
                if (i % 600 == 599) GD.Print($"CITY navigation at={giant.GlobalPosition} vel={giant.Velocity}");
            }
            Check(giant.GlobalPosition.DistanceTo(target.Position) < 15f, "full-size giant navigates around a tower");
            if (_capture is not null)
            {
                giant.Spawn(new Vector3(0f, 0f, -48f));
                _player.Teleport(new Vector3(18f, 33f, 22f));
                PlayerCamera.Instance!.FaceDirection(new Vector3(-0.2f, 0f, -1f));
                PlayerCamera.Instance.SetPitchDegrees(-20f); PlayerCamera.Instance.SnapToTarget();
                await Wait(0.4f);
                await Screenshot.Capture(GetTree(), this, _capture + "-skyline.png");
                _player.Teleport(new Vector3(0f, 12f, -13f));
                PlayerCamera.Instance.FaceDirection(new Vector3(-0.6f, 0f, -1f));
                PlayerCamera.Instance.SetPitchDegrees(5f); PlayerCamera.Instance.SnapToTarget();
                await Wait(0.3f);
                await Screenshot.Capture(GetTree(), this, _capture + "-giant.png");
                _player.Teleport(Vector3.Up * 0.2f);
                PlayerCamera.Instance.FaceDirection(new Vector3(18f, 0f, -16f));
                PlayerCamera.Instance.SetPitchDegrees(25f);
                PlayerCamera.Instance.SnapToTarget();
                Input.ActionPress("city_hook_left"); _player.SetPhysicsProcess(true);
                _player.AttachHook(anchor);
                await Wait(0.6f);
                Check(_player.Grappling, "rendered traversal uses a live tether");
                await Screenshot.Capture(GetTree(), this, _capture + "-tether.png");
                Input.ActionRelease("city_hook_left");
            }
            _player.Teleport(Vector3.Zero);
            Check(!_player.Grappling, "teleport resets the tether");
            _player.SetPhysicsProcess(true);
            Arena.UseBounds(Stages.Lumenreef); await Wait(0.06f);
            Check(_player.IsSwimming, "reef mode remains available to the city hero");
            Arena.UseBounds(Stages.Bellspire); await Wait(0.06f);
            Check(!_player.IsSwimming && PlayerCamera.Instance!.MaxPitchDegrees >= 80f,
                "reef-to-city transition restores upward hook aiming");
            Main main = Main.Instance!;
            EnemyManager.DevFrozen = false;
            manager.ClearField(20f);
            Check(main.Stages.DevBossNow(), "city boss encounter starts through the phase director");
            for (int i = 0; i < 50 && manager.ActiveBoss is null; i++) await Wait(0.1f);
            Enemy? boss = manager.ActiveBoss;
            Check(boss?.Def?.Id == "city_archon", "phase director spawns the colossal city archon");
            if (boss is not null) boss.TakeDamage(new Combat.DamageInfo { Amount = 1000000f, Source = "city_check" });
            await Wait(0.2f);
            Check(main.Stages.State == StageDirector.PhaseState.PortalOpen, "defeating the city boss opens the real portal");
            main.Stages.DevJumpToPhase(2); await Wait(0.1f);
            Check(!_player.Grappling && !Arena.IsCity && Arena.Current.Id == "stormwatch",
                "real phase transition resets tether and preserves the harbour route");
            main.Stages.DevSetMap(Stages.Run[0]);
            main.Stages.DevBossNow();
            for (int i = 0; i < 50 && manager.ActiveBoss is null; i++) await Wait(0.1f);
            Check(manager.ActiveBoss is { IsCityGiant: false }, "ordinary-map boss selection excludes the city archon");
            Check(manager.SpawnAt(Catalog.EnemyById("city_bellwarden")!, Vector3.Zero) is null,
                "scripted spawns cannot leak city giants into ordinary maps");
        }
        catch (Exception e) { Check(false, e.ToString()); }
        finally
        {
            Input.ActionRelease("city_hook_left");
            Input.ActionRelease("dash");
            EnemyManager.DevFrozen = false;
            GD.Print(_passed ? "CITY PASS" : "CITY FAIL");
            GetTree().Quit(_passed ? 0 : 1);
        }
    }
}
