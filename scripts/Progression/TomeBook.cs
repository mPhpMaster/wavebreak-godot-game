using System;
using System.Collections.Generic;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Progression;

/// <summary>Published when a tome is learned or levelled. Carries the id
/// rather than the def so a listener never holds content it does not need.</summary>
public readonly struct TomeAcquired
{
    public readonly string Id;
    public readonly int NewLevel;
    public TomeAcquired(string id, int newLevel) { Id = id; NewLevel = newLevel; }
}

/// <summary>
/// The tomes held this run and their levels.
///
/// Same rule as <see cref="Inventory"/> items: a level change removes every
/// modifier tagged with the <see cref="TomeDef"/> and writes the new total back
/// as one modifier, so the sheet is a pure function of the levels and cannot
/// drift. The def is the source tag; no tome can touch another's modifier.
/// </summary>
public sealed class TomeBook
{
    /// <summary>The live run's book, or null outside a run.</summary>
    public static TomeBook? Current { get; private set; }

    private readonly StatSheet _stats;
    private readonly List<TomeDef> _order = new();
    private readonly Dictionary<string, int> _levels = new(StringComparer.Ordinal);

    /// <param name="publish">False for a scratch book (the content audit), which
    /// must not replace the run's <see cref="Current"/> or announce anything.</param>
    public TomeBook(StatSheet stats, bool publish = true)
    {
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _publish = publish;
        if (publish) Current = this;
    }

    private readonly bool _publish;

    /// <summary>Tome slots. A constant today; a property so an unlock can widen it.</summary>
    public int Slots { get; set; } = Tomes.MaxSlots;

    /// <summary>Held tomes, in the order they were learned.</summary>
    public IReadOnlyList<TomeDef> Held => _order;

    public int Count => _order.Count;

    public bool IsFull => _order.Count >= Slots;

    public int LevelOf(TomeDef? def) => def is not null && _levels.TryGetValue(def.Id, out int l) ? l : 0;

    public int LevelOf(string id) => _levels.TryGetValue(id, out int l) ? l : 0;

    /// <summary>True when taking this tome would change anything: an owned tome
    /// below its cap, or a new one with a slot free.</summary>
    public bool CanTake(TomeDef? def)
    {
        if (def is null) return false;
        int level = LevelOf(def);
        return level > 0 ? level < def.MaxLevel : !IsFull;
    }

    /// <summary>Learn or level a tome. False, and nothing changed, when it is
    /// maxed or there is no slot for a new one.</summary>
    public bool Add(TomeDef? def)
    {
        if (!CanTake(def)) return false;

        int level = LevelOf(def!) + 1;
        if (level == 1) _order.Add(def!);
        _levels[def!.Id] = level;

        _stats.RemoveAllFrom(def);
        if (def.Op != ModifierOp.Multiplicative || def.PerLevel > 0f)
            _stats.Add(def.Stat, def.Op, def.ValueAtLevel(level), def);
        TomeExtras.Apply(def, _stats, level);   // multi-stat tomes (Data/Tomes.Expansion.cs)

        if (_publish) EventBus.Publish(new TomeAcquired(def.Id, level));
        return true;
    }

    /// <summary>Drop every tome's modifiers. For a scratch book.</summary>
    public void Clear()
    {
        foreach (TomeDef def in _order) _stats.RemoveAllFrom(def);
        _order.Clear();
        _levels.Clear();
    }
}
