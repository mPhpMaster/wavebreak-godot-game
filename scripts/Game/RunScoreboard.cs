using System.Collections.Generic;
using Godot;
using Wavebreak.Core;
using Wavebreak.Net;

namespace Wavebreak.Game;

/// <summary>One player's numbers for the run.</summary>
public sealed class ScoreLine
{
    public long Id;
    public string Name = "";
    public int Slot;
    public int Kills, BossKills, Downs, Revives, Level = 1, GoldEarned;
    public float DamageDealt, DamageTaken;
}

/// <summary>
/// Per-player run statistics for the scoreboard (hold Y) and the results
/// screen.
///
/// Who counts what follows who owns it, as the rest of co-op does:
/// <list type="bullet">
/// <item>Kills, boss kills and damage dealt are counted where enemy health
/// lives: on the host (and in single-player, on this machine). Every hit
/// on the host carries its sender (<c>Enemy.LastHitter</c>), so the
/// killing blow is credited to whoever landed it.</item>
/// <item>Downs, level, damage taken and gold earned belong to each player's
/// own machine; clients send theirs to the host once a second.</item>
/// <item>Revives given are reported by the downed machine, which knows who
/// stood next to it when the revive filled.</item>
/// </list>
/// The host merges everything and sends the whole table back once a second
/// (<c>NetSession.Awareness</c>). Single-player has one line: this player.
///
/// Static and reset by <see cref="Begin"/> at every run start, like the
/// event bus; nothing here outlives the run scene in a way that matters.
/// </summary>
public static class RunScoreboard
{
    private static readonly Dictionary<long, ScoreLine> Lines = new();
    private static readonly List<ScoreLine> Sorted = new();
    private static bool _dirty = true;

    // This machine's own numbers.
    private static float _localTaken;
    private static int _localGold;

    /// <summary>Bumped on every change; views redraw when it moves.</summary>
    public static int Version { get; private set; }

    /// <summary>A new run: forget the last one and listen for this player's own numbers.</summary>
    public static void Begin()
    {
        Lines.Clear();
        _localTaken = 0f;
        _localGold = 0;
        _dirty = true;
        Version++;
        EventBus.Unsubscribe<PlayerDamaged>(OnDamaged);
        EventBus.Unsubscribe<GoldGained>(OnGold);
        EventBus.Subscribe<PlayerDamaged>(OnDamaged);
        EventBus.Subscribe<GoldGained>(OnGold);
        Line(Coop.LocalId);
    }

    private static void OnDamaged(PlayerDamaged e)
    {
        if (e.Info is { Nullified: false } info && info.Amount > 0f) _localTaken += info.Amount;
    }

    private static void OnGold(GoldGained e)
    {
        if (e.Amount > 0) _localGold += e.Amount;
    }

    /// <summary>Does this machine own enemy health (host or single-player)?</summary>
    private static bool Authority => !Coop.IsClient;

    public static ScoreLine Line(long id)
    {
        if (!Lines.TryGetValue(id, out ScoreLine? line))
        {
            line = new ScoreLine { Id = id };
            Lines[id] = line;
            _dirty = true;
        }
        if (Coop.Peer(id) is { } peer)
        {
            line.Name = peer.Name;
            line.Slot = peer.Slot;
        }
        else if (!Coop.Active && id == Coop.LocalId) line.Name = NetSession.PlayerName;
        else if (line.Name.Length == 0) line.Name = Coop.NameOf(id);
        return line;
    }

    /// <summary>Lines ordered by slot (the host first).</summary>
    public static IReadOnlyList<ScoreLine> All
    {
        get
        {
            if (_dirty)
            {
                Sorted.Clear();
                Sorted.AddRange(Lines.Values);
                Sorted.Sort((a, b) => a.Slot != b.Slot ? a.Slot.CompareTo(b.Slot) : a.Id.CompareTo(b.Id));
                _dirty = false;
            }
            return Sorted;
        }
    }

    // ------------------------------------------------------------ authority side

    /// <summary>Host / single-player: <paramref name="by"/> dealt <paramref name="amount"/> to an enemy.</summary>
    public static void NoteHit(long by, float amount)
    {
        if (!Authority || by == 0 || amount <= 0f) return;
        Line(by).DamageDealt += amount;
    }

    /// <summary>Host / single-player: <paramref name="by"/> landed a killing blow.</summary>
    public static void NoteKill(long by, bool boss)
    {
        if (!Authority || by == 0) return;
        ScoreLine l = Line(by);
        l.Kills++;
        if (boss) l.BossKills++;
        Version++;
    }

    /// <summary>Host: <paramref name="helper"/> revived someone.</summary>
    public static void NoteRevive(long helper)
    {
        if (!Authority || helper == 0) return;
        Line(helper).Revives++;
        Version++;
    }

    /// <summary>Host: a client's own numbers arrived.</summary>
    public static void ApplySelf(long id, int downs, int level, int taken, int gold)
    {
        ScoreLine l = Line(id);
        l.Downs = downs;
        l.Level = level;
        l.DamageTaken = taken;
        l.GoldEarned = gold;
        Version++;
    }

    // ------------------------------------------------------------ this player

    /// <summary>Copy this machine's own numbers into its line.</summary>
    public static void RefreshLocal()
    {
        ScoreLine l = Line(Coop.LocalId);
        (int downs, int level, int taken, int gold) = LocalSelf();
        if (l.Downs != downs || l.Level != level || (int)l.DamageTaken != taken || l.GoldEarned != gold) Version++;
        l.Downs = downs;
        l.Level = level;
        l.DamageTaken = taken;
        l.GoldEarned = gold;
    }

    /// <summary>This player's downs (a death counts in single-player), level,
    /// damage taken and gold earned.</summary>
    public static (int Downs, int Level, int Taken, int Gold) LocalSelf()
    {
        Actors.Player? me = Actors.Player.Instance;
        bool valid = me is not null && GodotObject.IsInstanceValid(me) && me.Health is not null;
        int downs = valid ? me!.Health.Downs : 0;
        if (!Coop.Active && valid && !me!.IsAlive) downs = Mathf.Max(downs, 1);
        int level = Progression.Inventory.Current?.Level ?? 1;
        return (downs, level, Mathf.RoundToInt(_localTaken), _localGold);
    }

    // ------------------------------------------------------------ sync

    private const int Stride = 9;

    /// <summary>Host: the whole table, for <c>NetSession.ScoreSync</c>.</summary>
    public static long[] Pack()
    {
        RefreshLocal();
        var d = new long[Lines.Count * Stride];
        int i = 0;
        foreach (ScoreLine l in Lines.Values)
        {
            d[i++] = l.Id; d[i++] = l.Kills; d[i++] = l.BossKills; d[i++] = l.Downs; d[i++] = l.Revives;
            d[i++] = l.Level; d[i++] = (long)l.DamageDealt; d[i++] = (long)l.DamageTaken; d[i++] = l.GoldEarned;
        }
        return d;
    }

    /// <summary>Client: the host's table replaces ours; our own self numbers
    /// are then laid back on top (they are fresher here).</summary>
    public static void Apply(long[] d)
    {
        // The host's table is the whole truth: a line it does not have (an
        // id from before a host migration) goes.
        var ids = new HashSet<long> { Coop.LocalId };
        for (int i = 0; i + Stride <= d.Length; i += Stride) ids.Add(d[i]);
        var stale = new List<long>();
        foreach (long id in Lines.Keys) if (!ids.Contains(id)) stale.Add(id);
        foreach (long id in stale) Lines.Remove(id);
        if (stale.Count > 0) _dirty = true;
        for (int i = 0; i + Stride <= d.Length; i += Stride)
        {
            ScoreLine l = Line(d[i]);
            l.Kills = (int)d[i + 1]; l.BossKills = (int)d[i + 2]; l.Downs = (int)d[i + 3]; l.Revives = (int)d[i + 4];
            l.Level = (int)d[i + 5]; l.DamageDealt = d[i + 6]; l.DamageTaken = d[i + 7]; l.GoldEarned = (int)d[i + 8];
        }
        RefreshLocal();
        Version++;
    }

    /// <summary>Host migration: a player's line moves to their new peer id.</summary>
    public static void Rekey(long from, long to)
    {
        if (from == to || !Lines.Remove(from, out ScoreLine? line)) return;
        if (Lines.TryGetValue(to, out ScoreLine? clash))
        {
            // Should not happen; keep the line with more on it.
            if (clash.Kills + clash.Level > line.Kills + line.Level) return;
        }
        line.Id = to;
        if (Coop.NameOf(to) is { } name && !name.StartsWith('#')) line.Name = name;
        Lines[to] = line;
        _dirty = true;
        Version++;
    }

    /// <summary>"950", "12.3k", "1.2M": damage numbers grow large.</summary>
    public static string Short(float v)
    {
        if (v >= 1_000_000f) return $"{v / 1_000_000f:0.0}M";
        if (v >= 10_000f) return $"{v / 1000f:0}k";
        if (v >= 1000f) return $"{v / 1000f:0.0}k";
        return $"{v:0}";
    }
}
