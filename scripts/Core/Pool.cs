using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Core;

/// <summary>
/// Optional contract for pooled nodes.
///
/// The pool works without it, but a node that implements this gets two things:
/// it can ask whether it is currently parked (useful for "am I actually in the
/// world?" guards), and <see cref="Pool{T}.Release"/> becomes idempotent. That
/// second one matters more than it looks — a death that can be triggered from
/// two places (health hitting zero and a despawn sweep) would otherwise push the
/// same node onto the free list twice and hand it out to two owners at once.
/// </summary>
public interface IPooled
{
    /// <summary>True while the node is parked in its pool. Written by the pool;
    /// implementers should just store it.</summary>
    bool IsPooled { get; set; }
}

/// <summary>
/// A fixed-ceiling reuse pool for scene nodes.
///
/// Survivors-likes spawn and kill thousands of things per run. Instantiating a
/// node costs a scene-tree insertion, a physics-server registration and a
/// managed/native handle each time, and freeing it hands all of that back to be
/// re-done a second later — so the naive create/free loop spends most of its
/// budget on bookkeeping and hands the GC a steady drip of dead objects.
///
/// This pool keeps nodes alive in the tree and toggles them instead. Parked
/// nodes are hidden and have <see cref="Node.ProcessMode"/> set to
/// <see cref="Node.ProcessModeEnum.Disabled"/>, which is a single property that
/// silences _Process, _PhysicsProcess and input for the node and its whole
/// subtree — cheaper and harder to get wrong than toggling each flag, and it
/// does not fight a node that manages its own per-frame callbacks.
///
/// Collision is deliberately *not* touched here: what "inactive" means for a
/// body varies (layer masks, disabled shapes, teleporting out of the level), so
/// that stays the node's own business.
///
/// Not thread-safe, by design — everything here runs on the main loop.
/// </summary>
/// <typeparam name="T">The pooled node type.</typeparam>
public sealed class Pool<T> where T : Node3D
{
    private readonly Node _parent;
    private readonly Func<T> _factory;
    private readonly Stack<T> _free = new();
    private readonly List<T> _all = new();

    /// <summary>Hard ceiling on instances this pool will ever create. Reaching it
    /// makes <see cref="Get"/> return null rather than growing without bound: a
    /// spawn that quietly fails is a far better failure than a frame budget that
    /// quietly disappears.</summary>
    public int Capacity { get; }

    /// <summary>Instances created so far, parked or live.</summary>
    public int TotalCreated => _all.Count;

    /// <summary>Instances currently parked and ready to hand out.</summary>
    public int FreeCount => _free.Count;

    /// <summary>Instances currently handed out.</summary>
    public int ActiveCount => _all.Count - _free.Count;

    /// <summary>True when every instance is created and handed out.</summary>
    public bool Exhausted => _free.Count == 0 && _all.Count >= Capacity;

    /// <param name="parent">Node newly created instances are added under.</param>
    /// <param name="factory">Creates a bare instance. Must not add it to the tree;
    /// the pool does that.</param>
    /// <param name="capacity">Maximum instances ever created. Clamped to >= 1.</param>
    /// <param name="prewarm">Instances to build and park immediately.</param>
    public Pool(Node parent, Func<T> factory, int capacity = 256, int prewarm = 0)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Capacity = Math.Max(1, capacity);
        if (prewarm > 0) Prewarm(prewarm);
    }

    /// <summary>
    /// Build instances up front and park them.
    ///
    /// Worth doing for anything that spawns in bursts: the first wave of a run
    /// otherwise pays for every instantiation inside one frame, which is exactly
    /// the moment a player notices a hitch.
    /// </summary>
    public void Prewarm(int count)
    {
        int target = Math.Min(Capacity, _all.Count + Math.Max(0, count));
        while (_all.Count < target)
        {
            T item = Create();
            Park(item);
            _free.Push(item);
        }
    }

    /// <summary>
    /// Hand out an instance, creating one if the pool is empty and under
    /// capacity. Returns null at capacity — callers are expected to treat that
    /// as "not right now" and move on, not as an error.
    /// </summary>
    public T? Get()
    {
        T? item = null;

        // Discard anything freed behind the pool's back (scene reload, QueueFree
        // from elsewhere) rather than handing out a dangling handle.
        while (_free.Count > 0)
        {
            T candidate = _free.Pop();
            if (GodotObject.IsInstanceValid(candidate)) { item = candidate; break; }
            _all.Remove(candidate);
        }

        if (item is null)
        {
            if (_all.Count >= Capacity) return null;
            item = Create();
        }

        if (item is IPooled pooled) pooled.IsPooled = false;
        item.ProcessMode = Node.ProcessModeEnum.Inherit;
        item.Visible = true;
        return item;
    }

    /// <summary>
    /// Park an instance. Safe to call twice on the same node when it implements
    /// <see cref="IPooled"/>; without that interface a double release corrupts
    /// the free list, so implement it on anything with more than one death path.
    /// </summary>
    public void Release(T? item)
    {
        if (item is null || !GodotObject.IsInstanceValid(item)) return;

        if (item is IPooled pooled)
        {
            if (pooled.IsPooled) return;
            pooled.IsPooled = true;
        }

        Park(item);
        _free.Push(item);
    }

    /// <summary>Park every live instance. Used when a run ends.</summary>
    public void ReleaseAll()
    {
        for (int i = 0; i < _all.Count; i++)
        {
            T item = _all[i];
            if (!GodotObject.IsInstanceValid(item)) continue;
            if (item is IPooled { IsPooled: true }) continue;
            Release(item);
        }
    }

    /// <summary>Free every instance and forget them. The pool is reusable
    /// afterwards; it simply starts empty again.</summary>
    public void Clear()
    {
        for (int i = 0; i < _all.Count; i++)
        {
            T item = _all[i];
            if (GodotObject.IsInstanceValid(item)) item.QueueFree();
        }
        _all.Clear();
        _free.Clear();
    }

    /// <summary>Every instance this pool owns, parked or live. Read-only; for
    /// debug overlays and bulk inspection.</summary>
    public IReadOnlyList<T> All => _all;

    private T Create()
    {
        T item = _factory();
        _all.Add(item);
        if (item.GetParent() is null) _parent.AddChild(item);
        return item;
    }

    private static void Park(T item)
    {
        if (item is IPooled pooled) pooled.IsPooled = true;
        item.Visible = false;
        item.ProcessMode = Node.ProcessModeEnum.Disabled;
    }
}
