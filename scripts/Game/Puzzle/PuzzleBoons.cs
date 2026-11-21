using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Stats;

namespace Wavebreak.Game.Puzzle;

/// <summary>What a solved puzzle room gives every player.</summary>
public enum PuzzleBoon
{
    Swift,
    Spring,
    Leap,
    Dash,
    Vigor,
    Ward,
}

/// <summary>
/// Puzzle boons: each time the team solves a puzzle room, every player gets
/// a power-up for the puzzles (faster feet, an extra air jump, a higher jump,
/// a quicker dash, more health and a heal, armour against spikes and guards).
/// They stack through the puzzle map, and through the whole Puzzle Gauntlet,
/// and are taken away when the map (or the gauntlet) ends.
///
/// <b>Who decides.</b> The host solves rooms (the shared
/// <see cref="PuzzleState"/>); which boon a room gives is fixed by the run's
/// seed and the room's place in the run's line of puzzle rooms
/// (<see cref="For"/>), so every machine, a late joiner and a client whose
/// map was rebuilt name the same boon for the same room without a message,
/// and each applies it to its own player once (<see cref="OnSolved"/>,
/// remembered by map seed and room).
///
/// The line is a shuffle bag of the six per run; a boon that hit its cap
/// (<see cref="Cap"/>) in the current stretch gives Vigor instead.
/// </summary>
public static class PuzzleBoons
{
    public static readonly PuzzleBoon[] All = Enum.GetValues<PuzzleBoon>();

    // Strength of one stack.
    public const float SwiftPerStack = 0.08f;
    public const float LeapPerStack = 0.12f;
    public const float DashCooldownPerStack = 0.82f;
    public const float VigorHealth = 10f;
    public const float VigorHeal = 0.4f;
    public const float WardArmour = 6f;

    public const string BannerTitle = "Puzzle boon: {0}";
    public const string BannerKeep = "Lasts until this puzzle map ends.";
    public const string BannerKeepGauntlet = "Lasts for the rest of the gauntlet.";
    public const string ListTitle = "Boons";

    public static string Title(PuzzleBoon b) => b switch
    {
        PuzzleBoon.Swift => "Swift Feet",
        PuzzleBoon.Spring => "Spring Heels",
        PuzzleBoon.Leap => "High Leap",
        PuzzleBoon.Dash => "Quick Dash",
        PuzzleBoon.Vigor => "Vigor",
        PuzzleBoon.Ward => "Stone Skin",
        _ => "",
    };

    public static string Blurb(PuzzleBoon b) => b switch
    {
        PuzzleBoon.Swift => "+8% move speed.",
        PuzzleBoon.Spring => "+1 air jump.",
        PuzzleBoon.Leap => "+12% jump height.",
        PuzzleBoon.Dash => "The dash recharges 18% faster.",
        PuzzleBoon.Vigor => "+10 max health, and a 40% heal.",
        PuzzleBoon.Ward => "+6 armour: spikes and guards hurt less.",
        _ => "",
    };

    /// <summary>Stacks one stretch may hold (Vigor has none).</summary>
    public static int Cap(PuzzleBoon b) => b switch
    {
        PuzzleBoon.Swift => 4,
        PuzzleBoon.Spring => 2,
        PuzzleBoon.Leap => 3,
        PuzzleBoon.Dash => 3,
        PuzzleBoon.Ward => 4,
        _ => int.MaxValue,
    };

    /// <summary>Every string, for the content audit and the Arabic table.</summary>
    public static IEnumerable<string> Strings()
    {
        yield return BannerTitle;
        yield return BannerKeep;
        yield return BannerKeepGauntlet;
        yield return ListTitle;
        foreach (PuzzleBoon b in All)
        {
            yield return Title(b);
            yield return Blurb(b);
        }
    }

    // ------------------------------------------------------------ choice

    /// <summary>Where a map's run of boons starts: the gauntlet keeps one
    /// stretch for all of its maps; on the puzzle route each map is its own.</summary>
    public static int StretchStart(PuzzlePlan plan) => plan.Gauntlet ? 0 : plan.BagOffset;

    /// <summary>The boon puzzle room <paramref name="room"/> of this map gives
    /// (the same on every machine).</summary>
    public static PuzzleBoon For(PuzzlePlan plan, int room)
        => Line(plan.RunSeed, StretchStart(plan), plan.BagOffset + Math.Max(0, room - 1));

    /// <summary>Boon number <paramref name="ordinal"/> of the run, in a
    /// stretch that started at <paramref name="from"/> (caps count from there).</summary>
    public static PuzzleBoon Line(ulong runSeed, int from, int ordinal)
    {
        var held = new int[All.Length];
        PuzzleBoon pick = PuzzleBoon.Vigor;
        var bag = new List<PuzzleBoon>();
        int bagNo = -1;
        from = Math.Clamp(from, 0, ordinal);
        for (int j = from; j <= ordinal; j++)
        {
            // Bags count from the stretch's start, so its first six differ.
            int b = (j - from) / All.Length;
            if (b != bagNo)
            {
                bagNo = b;
                bag.Clear();
                bag.AddRange(All);
                PuzzleGen.Shuffle(new Rng(runSeed ^ 0xB0_0B5EEDUL ^ (ulong)(from * 31 + b + 1) * 0x9E3779B97F4A7C15UL), bag);
            }
            pick = bag[(j - from) % All.Length];
            if (held[(int)pick] >= Cap(pick)) pick = PuzzleBoon.Vigor;
            held[(int)pick]++;
        }
        return pick;
    }

    // ------------------------------------------------------------- state

    private static readonly int[] _stacks = new int[All.Length];
    private static readonly HashSet<(ulong, int)> _granted = new();
    private static Player? _player;
    private static int _airAdded;
    private static object? _run;
    private static ulong _stretch;
    private static bool _stretchSet;

    /// <summary>Stacks held now.</summary>
    public static int Stacks(PuzzleBoon b) => _stacks[(int)b];

    /// <summary>Boons held now (every stack).</summary>
    public static int Total
    {
        get
        {
            int n = 0;
            foreach (int s in _stacks) n += s;
            return n;
        }
    }

    /// <summary>Boons given since the game started (checks).</summary>
    public static int GrantedEver { get; private set; }

    /// <summary>Raised on this machine when its player gets a boon.</summary>
    public static event Action<PuzzleBoon, bool>? Granted;

    // One source object per boon, so taking them away is one call each.
    private static readonly object[] Sources = { new(), new(), new(), new(), new(), new() };

    private static ulong StretchKey(PuzzlePlan plan) => plan.Gauntlet ? plan.RunSeed ^ 0x6A0_7E7UL : plan.Seed;

    /// <summary>Every frame of a puzzle map (every machine): a new run, a new
    /// player or another stretch (a later puzzle map on the route) starts
    /// clean.</summary>
    public static void Track(PuzzlePlan plan)
    {
        object? run = Main.Instance?.Run;
        Player? me = Player.Instance;
        if (!ReferenceEquals(run, _run) || me is not null && _player is not null && !ReferenceEquals(me, _player))
        {
            // Take them off a player that is still about (a restart that kept it).
            if (_player is not null && GodotObject.IsInstanceValid(_player)) Clear();
            Forget();
            _run = run;
        }
        ulong key = StretchKey(plan);
        if (!_stretchSet || key != _stretch)
        {
            Clear();
            _stretch = key;
            _stretchSet = true;
        }
    }

    /// <summary>Room <paramref name="room"/> of this map was solved: give its
    /// boon to this machine's player, once.</summary>
    public static bool OnSolved(PuzzlePlan plan, int room)
    {
        if (!_granted.Add((plan.Seed, room))) return false;
        PuzzleBoon b = For(plan, room);
        Grant(b);
        Granted?.Invoke(b, plan.Gauntlet);
        return true;
    }

    /// <summary>Add one stack of <paramref name="b"/> to this machine's player.</summary>
    public static void Grant(PuzzleBoon b)
    {
        _stacks[(int)b]++;
        GrantedEver++;
        Player? p = Player.Instance;
        if (p is null || !GodotObject.IsInstanceValid(p)) return;
        _player = p;
        object src = Sources[(int)b];
        switch (b)
        {
            case PuzzleBoon.Swift:
                p.Stats.Add(Stat.MoveSpeed, ModifierOp.Additive, SwiftPerStack, src);
                break;
            case PuzzleBoon.Spring:
                p.ExtraAirJumps += 1;
                _airAdded++;
                break;
            case PuzzleBoon.Leap:
                p.Stats.Add(Stat.JumpHeight, ModifierOp.Additive, LeapPerStack, src);
                break;
            case PuzzleBoon.Dash:
                p.Stats.Add(Stat.DashCooldown, ModifierOp.Multiplicative, DashCooldownPerStack, src);
                p.RefundDash(10f);
                break;
            case PuzzleBoon.Vigor:
                p.Stats.Add(Stat.MaxHealth, ModifierOp.Flat, VigorHealth, src);
                p.Health.HealFraction(VigorHeal);
                break;
            case PuzzleBoon.Ward:
                p.Stats.Add(Stat.Armor, ModifierOp.Flat, WardArmour, src);
                break;
        }
        GD.Print($"[puzzle] boon {b} x{_stacks[(int)b]} (held {Total})");
    }

    /// <summary>The map is being left (every machine): the route's boons end
    /// with their map, the gauntlet's with its last.</summary>
    public static void EndMap(PuzzlePlan plan)
    {
        if (plan.Gauntlet && plan.PuzzleIndex < Meta.Routes.GauntletPuzzles - 1) return;
        Clear();
        _stretchSet = false;
    }

    /// <summary>Take every boon off this machine's player.</summary>
    public static void Clear()
    {
        if (Total > 0) GD.Print($"[puzzle] boons end (held {Total})");
        Player? p = _player;
        if (p is not null && GodotObject.IsInstanceValid(p))
        {
            foreach (object src in Sources) p.Stats.RemoveAllFrom(src);
            p.ExtraAirJumps = Math.Max(0, p.ExtraAirJumps - _airAdded);
        }
        Array.Clear(_stacks);
        _airAdded = 0;
        _granted.Clear();
    }

    /// <summary>Boon modifiers on a player's stat sheet (checks).</summary>
    public static int ModifiersOn(Player p)
    {
        int n = 0;
        foreach (StatModifier m in p.Stats.Modifiers)
            if (Array.IndexOf(Sources, m.Source) >= 0) n++;
        return n;
    }

    /// <summary>A new run or player: nothing to take off (the old player is gone).</summary>
    private static void Forget()
    {
        _player = null;
        Array.Clear(_stacks);
        _airAdded = 0;
        _granted.Clear();
        _stretchSet = false;
    }
}
