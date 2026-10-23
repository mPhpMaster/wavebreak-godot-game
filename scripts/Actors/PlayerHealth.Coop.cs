using Godot;
using Wavebreak.Core;

namespace Wavebreak.Actors;

/// <summary>Co-op down and revive. Single-player never reaches either: a
/// death there ends the run as it always has.</summary>
public sealed partial class PlayerHealth
{
    private static readonly Color DownColor = new(1.00f, 0.45f, 0.40f);

    /// <summary>Times this player went down / was revived this run.</summary>
    public int Downs { get; private set; }
    public int Revives { get; private set; }

    private void GoDown()
    {
        Downs++;
        GD.Print($"[coop] local player down (t={Game.Main.Instance?.Run.Clock})");
        EventBus.Publish(new PopupRequested(PopupAnchor + Vector3.Up * 0.6f, Meta.Loc.T("DOWN! A teammate can revive you"), DownColor, 1.3f));
    }

    /// <summary>Back on your feet with <paramref name="fraction"/> of max health
    /// and a moment of immunity to get clear.</summary>
    public void Revive(float fraction)
    {
        if (!_dead) return;
        _dead = false;
        Revives++;
        Current = Mathf.Max(1f, Max * Mathf.Clamp(fraction, 0.05f, 1f));
        _invulnerable = 2f;
        _shieldDelay = 0f;
        GD.Print($"[coop] local player revived (t={Game.Main.Instance?.Run.Clock})");
        EventBus.Publish(new PopupRequested(PopupAnchor + Vector3.Up * 0.6f, Meta.Loc.T("REVIVED"), HealColor, 1.4f));
        EventBus.Publish(new PlayerHealed(Current));
    }
}
