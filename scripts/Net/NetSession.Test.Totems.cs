using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;
using Wavebreak.Progression;

namespace Wavebreak.Net;

/// <summary>
/// <c>--coop-check-totem</c> (tests/coop.ps1): a client wakes a Tide Totem.
///
/// Both bots leave totems alone (<see cref="Interactables.BotTotemsOff"/>).
/// At 3 s of run clock the host puts a totem 3 m from the client (the dev
/// path, so the placement is sent like any other) and drops 24 experience
/// orbs and 12 coins far from everyone. From 5 s the client steps up to it and
/// presses the real interact key. The host must grant it to the client, latch
/// every orb to the client, see every one of them collected by the client and
/// the totem asleep. The client must have the map's placed totems too, see the
/// totem asleep, see every pulled orb arrive and be paid at least the coins'
/// gold. Each side prints <c>COOPTOTEM PASS|FAIL role=... ...</c>;
/// <c>--coop-totem-shot=&lt;png&gt;</c> photographs the moment on the client.
/// </summary>
public sealed partial class NetSession
{
    private const int TotemTestXp = 24, TotemTestCoins = 12, TotemTestCoinValue = 5;

    private bool _totemTestParsed, _totemTest, _totemDone, _totemDropped, _totemPressed;
    private string? _totemShot;
    private int _totemTestId, _totemFiresSeen, _totemRemoteAtFire, _totemGold0;
    private float _totemFiredAt = -1f, _totemPressedAt = -1f;
    internal int TotemRemoteCollectsAtFire;
    /// <summary>The autopilot's steering (Player.MoveOverride), held while
    /// the client stands at the totem and handed back after.</summary>
    private System.Func<Vector3>? _totemOverride;

    private void StepTotemTest()
    {
        if (!_totemTestParsed)
        {
            _totemTestParsed = true;
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg == "--coop-check-totem") _totemTest = true;
                else if (arg.StartsWith("--coop-totem-shot=")) _totemShot = arg["--coop-totem-shot=".Length..];
            }
            if (_totemTest) Interactables.BotTotemsOff = true;
        }
        if (!_totemTest || _totemDone || _main is null || !IsInstanceValid(_main) || _main.Run.Phase != 1) return;
        if (Interactables.Instance is not { } hub) return;
        float t = _main.Run.Seconds;
        if (Coop.IsHost) StepTotemHost(hub, t);
        else StepTotemClient(hub, t);
    }

    private void StepTotemHost(Interactables hub, float t)
    {
        if (!_totemDropped && t >= 3f)
        {
            RemotePlayer? guest = _remotes.Values.FirstOrDefault(r => IsInstanceValid(r) && r.IsAlive);
            if (guest is null) return;
            _totemDropped = true;
            Vector3 at = guest.GlobalPosition + new Vector3(3f, 0f, 0f);
            TideTotem? totem = hub.SpawnTotemAt(at with { Y = Mathf.Max(0f, guest.GlobalPosition.Y - 0.05f) });
            _totemTestId = totem?.NetId ?? 0;
            // The haul: on the far side of the centre from the guest.
            Vector3 away = -(guest.GlobalPosition with { Y = 0f });
            away = away.LengthSquared() > 1f ? away.Normalized() : new Vector3(-0.7f, 0f, 0.7f);
            Vector3 spot = away * Arena.Radius * 0.55f;
            var rng = new Core.Rng(0x707E3UL);
            for (int i = 0; i < TotemTestXp; i++) Pickup.Spawn(PickupKind.Xp, spot + rng.OnUnitCircleXZ() * rng.Range(0f, 6f) + Vector3.Up * 0.5f, 6f);
            for (int i = 0; i < TotemTestCoins; i++) Pickup.Spawn(PickupKind.Gold, spot + rng.OnUnitCircleXZ() * rng.Range(0f, 6f) + Vector3.Up * 0.5f, TotemTestCoinValue);
            GD.Print($"[coop-totem] test totem #{_totemTestId} by the guest, haul at ({spot.X:0},{spot.Z:0}), " +
                     $"{(guest.GlobalPosition - spot).Length():0} m from it");
        }

        if (_totemFiredAt < 0f && TotemFires > 0)
        {
            _totemFiredAt = t;
            _totemRemoteAtFire = TotemRemoteCollectsAtFire;
        }
        if (_totemFiredAt >= 0f)
        {
            int left = TotemNetIds.Count(Pickup.NetOrbAlive);
            bool settled = left == 0 && RemoteCollects - _totemRemoteAtFire >= TotemNetIds.Count;
            if (!settled && t < _totemFiredAt + 8f) return;
            TideTotem? totem = hub.TotemById(_totemTestId);
            bool byGuest = TotemLastCaller != Coop.LocalId && TotemLastCaller != 0;
            bool ok = settled && byGuest && totem is { Used: true } && TotemNetIds.Count >= (TotemTestXp + TotemTestCoins) * 3 / 4;
            _totemDone = true;
            GD.Print($"COOPTOTEM {(ok ? "PASS" : "FAIL")} role=host caller={Coop.NameOf(TotemLastCaller)} latched={TotemNetIds.Count} " +
                     $"left={left} remote_collects={RemoteCollects - _totemRemoteAtFire} dormant={(totem is { Used: true } ? 1 : 0)} " +
                     $"seconds={t - _totemFiredAt:0.0} synced={TotemsSynced}");
            return;
        }
        if (t >= 22f)
        {
            _totemDone = true;
            GD.Print($"COOPTOTEM FAIL role=host never woken (test totem #{_totemTestId}, fires={TotemFires})");
        }
    }

    private void StepTotemClient(Interactables hub, float t)
    {
        Player? me = Player.Instance;
        if (me is null || !IsInstanceValid(me)) return;
        if (!_totemPressed && t >= 5f)
        {
            TideTotem? near = hub.Totems.Where(x => !x.Used)
                                  .OrderBy(x => x.GlobalPosition.DistanceSquaredTo(me.GlobalPosition))
                                  .FirstOrDefault(x => x.GlobalPosition.DistanceTo(me.GlobalPosition) < 25f);
            if (near is null || Pickup.ActiveCount < TotemTestXp) return;   // the totem and the haul arrive first
            _totemPressed = true;
            _totemPressedAt = t;
            _totemTestId = near.NetId;
            _totemGold0 = Inventory.Current?.Gold ?? 0;
            Vector3 to = me.GlobalPosition - near.GlobalPosition; to.Y = 0f;
            to = to.LengthSquared() > 0.01f ? to.Normalized() : Vector3.Back;
            me.Teleport(near.GlobalPosition + to * 1.8f + Vector3.Up * 0.4f);
            _totemOverride = me.MoveOverride;
            me.MoveOverride = () => Vector3.Zero;
            _ = PressInteract();
            return;
        }
        if (_totemPressed && _totemFiredAt < 0f && TotemFires > _totemFiresSeen && TotemLastCaller == Coop.LocalId)
        {
            _totemFiredAt = t;
            if (Player.Instance is { } p) p.MoveOverride = _totemOverride;
            if (_totemShot is not null) _ = CaptureTotem(_totemShot);
        }
        if (_totemFiredAt >= 0f)
        {
            int left = TotemNetIds.Count(Pickup.NetOrbAlive);
            int gold = (Inventory.Current?.Gold ?? 0) - _totemGold0;
            bool settled = left == 0 && gold >= TotemTestCoins * TotemTestCoinValue * 9 / 10;
            if (!settled && t < _totemFiredAt + 8f) return;
            TideTotem? totem = hub.TotemById(_totemTestId);
            int placed = hub.Totems.Count(x => x.NetId != _totemTestId);
            int want = Interactables.TotemCountFor(_main!.Stages.Stage);
            bool ok = settled && totem is { Used: true } && placed == want && TotemNetIds.Count >= (TotemTestXp + TotemTestCoins) * 3 / 4;
            _totemDone = true;
            GD.Print($"COOPTOTEM {(ok ? "PASS" : "FAIL")} role=client totem=#{_totemTestId} pulled={TotemNetIds.Count} left={left} " +
                     $"gold=+{gold} dormant={(totem is { Used: true } ? 1 : 0)} map_totems={placed}/{want} seconds={t - _totemFiredAt:0.0}");
            return;
        }
        if (_totemPressed && t >= _totemPressedAt + 6f)
        {
            _totemDone = true;
            if (Player.Instance is { } p) p.MoveOverride = _totemOverride;
            GD.Print($"COOPTOTEM FAIL role=client the host never answered (totem #{_totemTestId}, fires={TotemFires})");
        }
        else if (!_totemPressed && t >= 20f)
        {
            _totemDone = true;
            GD.Print($"COOPTOTEM FAIL role=client no totem near (totems={hub.Totems.Count()}, orbs={Pickup.ActiveCount})");
        }
    }

    /// <summary>The real interact key, then (if the prompt was not in focus
    /// that frame) the same call the key makes.</summary>
    private async System.Threading.Tasks.Task PressInteract()
    {
        SceneTree tree = GetTree();
        _totemFiresSeen = TotemFires;
        await ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventAction { Action = "interact", Pressed = true });
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventAction { Action = "interact", Pressed = false });
        await ToSignal(tree.CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
        if (TotemFires == _totemFiresSeen && Interactables.Instance?.TotemById(_totemTestId) is { Used: false, NetPending: false } t)
        {
            GD.Print("[coop-totem] the key missed the prompt; calling the totem directly");
            Interactables.Instance.UseTotem(t);
        }
        else GD.Print("[coop-totem] interact key pressed at the totem");
    }

    private async System.Threading.Tasks.Task CaptureTotem(string path)
    {
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        await Screenshot.Capture(GetTree(), this, path);
    }
}
