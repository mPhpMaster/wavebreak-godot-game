using System.Collections.Generic;
using Godot;

namespace Wavebreak.Net;

public enum CoopMode { None, Host, Client }

/// <summary>One player in a co-op session, as the lobby and the run know them.</summary>
public sealed class CoopPeer
{
    public long Id;
    public int Slot;
    public string Name = "";
    public string CharacterId = "";
    public string SkinId = "";
    public string HatId = "";
    public bool Ready;
}

/// <summary>
/// Co-op session facts, readable from anywhere without a node reference.
///
/// The one rule every hook in the game follows: when <see cref="Active"/> is
/// false nothing here changes behaviour. Single-player never creates a
/// <see cref="NetSession"/>, so <see cref="Mode"/> stays None and every
/// <c>if (Coop.Active)</c> branch is skipped.
/// </summary>
public static class Coop
{
    public const int DefaultPort = 7777;
    public const int MaxPlayers = 4;

    /// <summary>Protocol tag; a client from another build is turned away
    /// instead of desyncing halfway into a run.</summary>
    public const string Protocol = "wavebreak-coop-3";   // 2: hats and the host's difficulty in the lobby; 3: host migration, late join

    /// <summary>A teammate standing within this many metres of a downed
    /// player for <see cref="ReviveSeconds"/> brings them back.</summary>
    public const float ReviveRadius = 2.6f;
    public const float ReviveSeconds = 4f;

    /// <summary>Health a revived player comes back with, as a fraction of max.</summary>
    public const float ReviveHealth = 0.4f;

    /// <summary>While a menu is open in co-op the world keeps running; the
    /// player is immune for at most this long per opening (Bonk With Friends
    /// gives an 8 s level-up grace for the same reason).</summary>
    public const float MenuGraceSeconds = 8f;

    public static CoopMode Mode { get; internal set; }
    public static bool Active => Mode != CoopMode.None;
    public static bool IsHost => Mode == CoopMode.Host;
    public static bool IsClient => Mode == CoopMode.Client;

    /// <summary>True from the host's start signal until the run scene is left.</summary>
    public static bool InRun { get; internal set; }

    /// <summary>The host's difficulty tier id. The host picks it on the CO-OP
    /// page; it rides along with every roster update, so clients see it in the
    /// lobby, and the run starts on it (<see cref="Meta.DifficultyTiers.BeginRun"/>).</summary>
    public static string LobbyDifficulty => DifficultyId;

    /// <summary>Run seed, chosen by the host; every peer builds the same maps from it.</summary>
    public static ulong Seed { get; internal set; }
    public static string DifficultyId { get; internal set; } = "";

    /// <summary>The host started an Endless run (sent as an "endless:" prefix
    /// on the difficulty id, so the start RPC stays the same).</summary>
    public static bool Endless { get; internal set; }

    public static long LocalId { get; internal set; } = 1;
    public static readonly List<CoopPeer> Roster = new();

    /// <summary>Shown on the CO-OP page after the session ended under us
    /// (the host left, the connection failed).</summary>
    public static string? Notice { get; set; }

    public static CoopPeer? Peer(long id)
    {
        foreach (CoopPeer p in Roster) if (p.Id == id) return p;
        return null;
    }

    public static CoopPeer? LocalPeer => Peer(LocalId);

    public static string NameOf(long id) => Peer(id)?.Name ?? (Departed.TryGetValue(id, out string? n) ? n : $"#{id}");

    /// <summary>Players who hosted and left mid-run (host migration), by the
    /// id their scoreboard line now carries (<see cref="DepartedId"/>).</summary>
    public static readonly Dictionary<long, string> Departed = new();

    /// <summary>The scoreboard id of a host who left from <paramref name="slot"/>:
    /// negative, so it never meets a real peer id.</summary>
    public static long DepartedId(int slot) => -(slot + 1);

    // ------------------------------------------------------------ menu "pause"

    /// <summary>What <c>SceneTree.Paused</c> would be in single-player. In a
    /// co-op run the tree never pauses (the others are still playing); this
    /// flag freezes only the local player's input instead.</summary>
    public static bool LocalHold { get; private set; }

    private static float _holdSeconds;

    /// <summary>The local player is shielded while a menu holds them, for at
    /// most <see cref="MenuGraceSeconds"/> per opening.</summary>
    public static bool HoldShielded => LocalHold && _holdSeconds < MenuGraceSeconds;

    /// <summary>Pause or resume. Single-player: exactly <c>tree.Paused = paused</c>.</summary>
    public static void SetPaused(SceneTree tree, bool paused)
    {
        if (Active && InRun)
        {
            if (paused && !LocalHold) _holdSeconds = 0f;
            LocalHold = paused;
            return;
        }
        tree.Paused = paused;
    }

    /// <summary>Single-player: <c>tree.Paused</c>. Co-op run: the local hold.</summary>
    public static bool IsPaused(SceneTree tree) => Active && InRun ? LocalHold : tree.Paused;

    internal static void TickHold(float dt)
    {
        if (LocalHold) _holdSeconds += dt;
    }

    internal static void ClearHold()
    {
        LocalHold = false;
        _holdSeconds = 0f;
    }

    // ------------------------------------------------------------ placement

    /// <summary>Where this player stands relative to the map centre on
    /// arrival, so the team does not spawn inside each other. Zero in
    /// single-player.</summary>
    public static Vector3 SpawnOffset => Active && LocalPeer is { } me ? SlotOffset(me.Slot) : Vector3.Zero;

    public static Vector3 SlotOffset(int slot)
    {
        float a = slot * Mathf.Tau / MaxPlayers + 0.4f;
        return slot == 0 ? Vector3.Zero : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.2f;
    }

    /// <summary>Per-slot colour for name tags, rings and the team panel.</summary>
    public static Color SlotColor(int slot) => (slot % MaxPlayers) switch
    {
        0 => new Color(0.45f, 0.85f, 1.00f),
        1 => new Color(1.00f, 0.62f, 0.25f),
        2 => new Color(0.55f, 1.00f, 0.50f),
        _ => new Color(1.00f, 0.50f, 0.85f),
    };

    /// <summary>Back to single-player defaults.</summary>
    internal static void Reset()
    {
        Mode = CoopMode.None;
        InRun = false;
        Roster.Clear();
        Departed.Clear();
        LocalId = 1;
        DifficultyId = "";
        ClearHold();
    }
}
