using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Progression;
using Wavebreak.Stats;
using Wavebreak.UI;

namespace Wavebreak.Game;

public sealed partial class ShrineSpawner
{
    private void StartTrial(MapShrine s)
    {
        if (_trial != Trial.None)
        {
            s.Deny();
            _prompt.Deny();
            Publish(s, ShrineOutcome.Denied, "busy");
            return;
        }

        RunState? run = Main.Instance?.Run;
        EnemyDef? elite = PickElite(run?.Seconds ?? 0f);
        bool guardians = elite is not null && EnemyManager.Instance is not null && Rng.Chance(0.5f);

        _trialShrine = s;
        s.Active = true;
        _guardians.Clear();
        _guardiansDown = 0;

        if (guardians)
        {
            _trial = Trial.Guardians;
            _trialLeft = MapShrines.GuardianSeconds;
            int want = MapShrines.GuardianCount(run?.Phase ?? 1);
            float start = Rng.Range(0f, Mathf.Tau);
            for (int i = 0; i < want; i++)
            {
                float a = start + Mathf.Tau * i / want;
                Vector3 at = s.GlobalPosition + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * MapShrines.GuardianRing;
                if (s.GetParent() is Arena arena) at = arena.ClearSpotNear(at, 1.2f);
                at.Y = s.GlobalPosition.Y + 0.3f;
                Enemy? e = EnemyManager.Instance!.SpawnAt(elite!, at);
                if (e is not null) _guardians.Add(e);
            }
            _trialTarget = _guardians.Count;
            if (_trialTarget == 0)
            {
                // No room in the pool: fall back to the kill count.
                guardians = false;
            }
        }
        if (!guardians)
        {
            _trial = Trial.Slaughter;
            _trialLeft = MapShrines.SlaughterSeconds;
            _trialTarget = MapShrines.SlaughterTarget(run?.Seconds ?? 0f);
            _trialKillsAt = run?.Kills ?? 0;
        }

        string what = _trial == Trial.Slaughter ? $"KILL {_trialTarget} IN {MapShrines.SlaughterSeconds:0}s"
                                                : $"SLAY {_trialTarget} GUARDIANS";
        EventBus.Publish(new PopupRequested(s.Top, "CHALLENGE!", MapShrines.Tint(MapShrineKind.Challenge).Lightened(0.2f), 1.8f));
        // English "what" also goes to the shrine event below; the popup translates it.
        EventBus.Publish(new PopupRequested(s.Top + Vector3.Down * 0.7f, what, ShrinePrompt.Neutral, 1.1f));

        Publish(s, ShrineOutcome.ChallengeStarted, what);
        GD.Print($"[shrine] challenge {_trial} target={_trialTarget} t={run?.Seconds ?? 0f:0}s");
    }

    /// <summary>The strongest elite unlocked by now, else the earliest one.</summary>
    private static EnemyDef? PickElite(float runSeconds)
    {
        EnemyDef? best = null, earliest = null;
        foreach (EnemyDef def in Catalog.Enemies)
        {
            if (def is null || !def.IsElite || def.IsBoss || Visuals.CityGiantModels.IsGiant(def) != Arena.IsCity) continue;
            if (earliest is null || def.UnlockAtSeconds < earliest.UnlockAtSeconds) earliest = def;
            if (def.UnlockAtSeconds <= runSeconds && (best is null || def.Cost > best.Cost)) best = def;
        }
        return best ?? earliest;
    }

    private void TickTrial(float dt)
    {
        _trialLeft -= dt;

        if (_trial == Trial.Guardians)
        {
            // A guardian is done once it is no longer alive. Checked every frame,
            // before the pool could hand the node to a new spawn.
            for (int i = _guardians.Count - 1; i >= 0; i--)
            {
                Enemy e = _guardians[i];
                if (IsInstanceValid(e) && e.IsAlive) continue;
                _guardians.RemoveAt(i);
                _guardiansDown++;
            }
            if (_guardiansDown >= _trialTarget) { EndTrial(true); return; }
        }
        else if (KillsSinceStart >= _trialTarget) { EndTrial(true); return; }

        if (_trialLeft <= 0f) EndTrial(false);
    }

    private void EndTrial(bool won, bool quiet = false)
    {
        MapShrine? s = _trialShrine;
        Trial trial = _trial;
        _trial = Trial.None;
        _trialShrine = null;
        _guardians.Clear();

        bool shrineOk = s is not null && IsInstanceValid(s);
        if (shrineOk) { s!.Active = false; s.MarkSpent(); }
        if (quiet) return;

        Vector3 at = shrineOk ? s!.Top : (Player.Instance is { } pl ? pl.GlobalPosition + Vector3.Up * 2.5f : Vector3.Zero);
        if (!won)
        {
            ChallengesFailed++;
            EventBus.Publish(new PopupRequested(at, "CHALLENGE FAILED", new Color(1f, 0.35f, 0.3f), 1.5f));
            Publish(MapShrineKind.Challenge, ShrineOutcome.ChallengeFailed, at, trial.ToString());
            GD.Print($"[shrine] challenge {trial} failed");
            return;
        }

        ChallengesWon++;
        if (shrineOk) s!.Celebrate();
        string reward = PayReward(at);
        EventBus.Publish(new PopupRequested(at + Vector3.Up * 0.8f, "CHALLENGE COMPLETE", new Color(0.55f, 1f, 0.65f), 1.6f));
        Publish(MapShrineKind.Challenge, ShrineOutcome.ChallengeWon, at, reward);
        GD.Print($"[shrine] challenge {trial} won reward=\"{reward}\"");
    }

    /// <summary>A gilded chest's roll (same luck bonus and rarity floor) plus
    /// gold scaled by phase. Granted here, not through a chest, so the chest
    /// economy's counters and price ladder are untouched.</summary>
    private string PayReward(Vector3 at)
    {
        Inventory? inv = Inventory.Current;
        Player? player = Player.Instance;
        if (inv is null || player is null) return "nothing";

        int gold = MapShrines.RewardGoldBase + MapShrines.RewardGoldPerPhase * Math.Max(0, (Main.Instance?.Run.Phase ?? 1) - 1);
        inv.GrantGold(gold);

        Offer? offer = OfferPool.Current?.RollChest(inv, player.Stats, MapShrines.RewardLuckBonus, MapShrines.RewardFloor, Rng);
        if (offer is null)
        {
            inv.GrantGold(gold);
            EventBus.Publish(new PopupRequested(at, $"+{gold * 2} gold", new Color(1f, 0.78f, 0.25f), 1.4f));
            return "gold";
        }
        if (offer.Item is not null) inv.Add(offer.Item);
        else offer.Apply(player, inv);
        Color c = offer.IsBonus ? offer.Tint : Catalog.RarityColor(offer.Rarity);
        EventBus.Publish(new PopupRequested(at, offer.Title, c, 1.8f));
        EventBus.Publish(new PopupRequested(at + Vector3.Down * 0.7f, $"+{gold} gold", new Color(1f, 0.78f, 0.25f)));
        return offer.Title;
    }

}
