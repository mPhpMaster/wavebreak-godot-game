using System;
using System.Reflection;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Actors;

/// <summary>
/// Finds the player without compiling against it.
///
/// The player is owned by another system and enemies need exactly three things
/// from it: where it is, an <see cref="IDamageable"/> to hit, and
/// <see cref="Stat.EnemyScaling"/> for the difficulty curve. Binding to the
/// concrete type for that would make the enemy layer un-buildable and
/// un-testable on its own, and would hard-couple two systems over three reads.
///
/// So the lookup is done once and cached: a static <c>Player.Instance</c> if the
/// player type provides one, else the "player" scene group, else a node named
/// "Player". Every read is a cached field afterwards; a failed lookup is retried
/// on a timer rather than every frame, because a full tree scan per enemy per
/// frame is exactly the kind of cost that hides until there are three hundred
/// enemies.
/// </summary>
internal static class PlayerRef
{
    private const double RetryMilliseconds = 250.0;

    private static Node3D? _node;
    private static IDamageable? _damageable;
    private static StatSheet? _sheet;
    private static ulong _nextAttemptMsec;

    private static Func<object?>? _instanceGetter;
    private static bool _instanceGetterSearched;

    /// <summary>The player node, or null when there is not one yet.</summary>
    public static Node3D? Node
    {
        get
        {
            if (_node is not null && GodotObject.IsInstanceValid(_node) && _node.IsInsideTree())
                return _node;
            Forget();
            return Resolve();
        }
    }

    /// <summary>Player node and damage interface together, so callers resolve once.</summary>
    public static bool TryGet(out Node3D node, out IDamageable? damageable)
    {
        Node3D? n = Node;
        node = n!;
        damageable = _damageable;
        return n is not null;
    }

    /// <summary>
    /// The player's <see cref="Stat.EnemyScaling"/>, or 1 when there is no
    /// player. Items that trade danger for reward move this, and the director
    /// multiplies its whole difficulty curve by it.
    /// </summary>
    public static float EnemyScaling
    {
        get
        {
            if (Node is null) return 1f;
            if (_sheet is null) return 1f;
            float v = _sheet.Get(Stat.EnemyScaling);
            return v > 0.01f ? v : 0.01f;
        }
    }

    /// <summary>
    /// Damage returned to an enemy each time it swings at the player:
    /// <see cref="Stat.Thorns"/> scaled by the player's damage multiplier, or 0
    /// when there is no player.
    ///
    /// Scaled by the damage stat so a thorns build does not fall off a cliff as
    /// enemy health climbs with the difficulty curve — the same items that grow
    /// the weapons grow the barbs.
    /// </summary>
    public static float Thorns
    {
        get
        {
            if (Node is null || _sheet is null) return 0f;
            float t = _sheet.Get(Stat.Thorns);
            return t <= 0f ? 0f : t * MathF.Max(0f, _sheet.GetMultiplier(Stat.Damage));
        }
    }

    /// <summary>Drop the cache. Call when a run ends so the next run does not
    /// inherit a stale handle.</summary>
    public static void Forget()
    {
        _node = null;
        _damageable = null;
        _sheet = null;
    }

    private static Node3D? Resolve()
    {
        ulong now = Time.GetTicksMsec();
        if (now < _nextAttemptMsec) return null;
        _nextAttemptMsec = now + (ulong)RetryMilliseconds;

        Node3D? found = FromStaticInstance() ?? FromTree();
        if (found is null || !GodotObject.IsInstanceValid(found)) return null;

        _node = found;
        _damageable = found as IDamageable;
        _sheet = FindStatSheet(found);
        return _node;
    }

    /// <summary>Reads <c>Player.Instance</c> if the player type exposes one. This
    /// is the intended path; the tree searches below are the safety net for a
    /// player that has not been written yet or is named something else.</summary>
    private static Node3D? FromStaticInstance()
    {
        if (!_instanceGetterSearched)
        {
            _instanceGetterSearched = true;
            _instanceGetter = FindInstanceGetter();
        }
        return _instanceGetter?.Invoke() as Node3D;
    }

    private static Func<object?>? FindInstanceGetter()
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        Type[] types;
        try { types = typeof(PlayerRef).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = Array.Empty<Type>(); _ = e; }

        foreach (Type t in types)
        {
            if (t.Name != "Player" || !typeof(Node3D).IsAssignableFrom(t)) continue;

            PropertyInfo? prop = t.GetProperty("Instance", Flags);
            if (prop is not null && prop.CanRead && prop.GetIndexParameters().Length == 0)
                return () => prop.GetValue(null);

            FieldInfo? field = t.GetField("Instance", Flags);
            if (field is not null) return () => field.GetValue(null);
        }
        return null;
    }

    private static Node3D? FromTree()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root is null) return null;

        if (tree.GetFirstNodeInGroup("player") is Node3D grouped) return grouped;
        if (tree.GetFirstNodeInGroup("Player") is Node3D groupedUpper) return groupedUpper;

        // owned: false — nodes added at runtime have no scene owner, and a
        // procedurally built level has no owned children at all.
        return tree.Root.FindChild("Player", recursive: true, owned: false) as Node3D;
    }

    /// <summary>Pulls the player's stat sheet off whatever member happens to hold
    /// it. Done once per player instance, so the reflection cost is irrelevant
    /// and the enemy layer does not need to know the player's field names.</summary>
    private static StatSheet? FindStatSheet(Node3D player)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        Type t = player.GetType();

        for (Type? cursor = t; cursor is not null && cursor != typeof(Node3D); cursor = cursor.BaseType)
        {
            foreach (PropertyInfo p in cursor.GetProperties(Flags | BindingFlags.DeclaredOnly))
            {
                if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
                if (!typeof(StatSheet).IsAssignableFrom(p.PropertyType)) continue;
                try { if (p.GetValue(player) is StatSheet s) return s; }
                catch (TargetInvocationException) { /* property threw; keep looking */ }
            }
            foreach (FieldInfo f in cursor.GetFields(Flags | BindingFlags.DeclaredOnly))
            {
                if (!typeof(StatSheet).IsAssignableFrom(f.FieldType)) continue;
                if (f.GetValue(player) is StatSheet s) return s;
            }
        }
        return null;
    }
}

