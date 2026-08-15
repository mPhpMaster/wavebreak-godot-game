using System;
using System.Collections.Generic;

namespace Wavebreak.Core;

/// <summary>
/// Decoupled game events.
///
/// The obvious alternative — a static <c>Action</c> field per event, subscribed
/// to directly — works right up until a run restarts: handlers on destroyed
/// nodes stay subscribed, fire against freed objects, and the crash surfaces
/// somewhere unrelated. This bus exists so <see cref="Reset"/> can wipe every
/// subscription at the end of a run in one call, which makes that whole class of
/// bug impossible rather than merely rare.
///
/// Publishing to an event nobody listens to is free and legal — systems are
/// meant to be addable and removable without touching the publisher.
/// </summary>
public static class EventBus
{
    private static readonly Dictionary<Type, Delegate> Handlers = new();

    public static void Subscribe<T>(Action<T> handler) where T : struct
    {
        Type key = typeof(T);
        Handlers[key] = Handlers.TryGetValue(key, out Delegate? existing)
            ? Delegate.Combine(existing, handler)
            : handler;
    }

    public static void Unsubscribe<T>(Action<T> handler) where T : struct
    {
        Type key = typeof(T);
        if (!Handlers.TryGetValue(key, out Delegate? existing)) return;
        Delegate? left = Delegate.Remove(existing, handler);
        if (left is null) Handlers.Remove(key);
        else Handlers[key] = left;
    }

    public static void Publish<T>(in T evt) where T : struct
    {
        if (Handlers.TryGetValue(typeof(T), out Delegate? d) && d is Action<T> typed)
            typed(evt);
    }

    /// <summary>Drop every subscription. Call when a run ends.</summary>
    public static void Reset() => Handlers.Clear();

    public static int SubscriberCount<T>() where T : struct
        => Handlers.TryGetValue(typeof(T), out Delegate? d)
            ? d.GetInvocationList().Length : 0;
}
