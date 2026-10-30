using System;
using Godot;

namespace Wavebreak.Net;

/// <summary>
/// How much harder the tide gets per player in co-op. Every number is here.
///
/// Four players bring four sets of weapons to a director tuned for one, so
/// without this a full team simply melted the crowd. The tables are indexed by
/// player count; the first column is 1, so single-player and a team of one are
/// untouched.
///
/// <b>Who counts.</b> Health is fixed when an enemy spawns, so it follows the
/// players <em>connected</em>: a teammate going down must not cheapen the next
/// wave for a few seconds and leave it tough once they are up again. The crowd
/// (spawn income and live cap) reacts at once, so it follows the players
/// <em>living</em>: a team with someone down gets room to go and fetch them.
///
/// <b>Who applies it.</b> Only the machine running the director, which reads
/// its own roster and targets every tick. So nothing is sent or stored, and a
/// late joiner, a player leaving and a host migration all take effect on the
/// next tick. Enemies already on the field keep the health they spawned with.
/// </summary>
public static class CoopScaling
{
    //                                           players:  1      2      3      4
    private static readonly float[] HealthTable =        { 1.00f, 1.30f, 1.60f, 1.90f };   // every enemy's health
    private static readonly float[] BossHealthTable =    { 1.00f, 1.60f, 2.20f, 2.80f };   // a boss, in place of the row above: one body takes every weapon
    private static readonly float[] SpawnRateTable =     { 1.00f, 1.45f, 1.90f, 2.35f };   // director credit income and bank
    private static readonly float[] CrowdTable =         { 1.00f, 1.25f, 1.50f, 1.75f };   // live cap, under the frame budget's ceiling

    private static float Row(float[] table, int players) => table[Math.Clamp(players, 1, table.Length) - 1];

    public static float Health(int connected) => Row(HealthTable, connected);
    public static float BossHealth(int connected) => Row(BossHealthTable, connected);
    public static float SpawnRate(int living) => Row(SpawnRateTable, living);
    public static float Crowd(int living) => Row(CrowdTable, living);

    /// <summary>Players in the run, downed ones included; 1 outside co-op.</summary>
    public static int Connected => Coop.InRun ? Math.Max(1, Coop.Roster.Count) : 1;

    /// <summary>Players up right now, as the host's enemy targets count them;
    /// 1 outside co-op and when everyone is down.</summary>
    public static int Living { get; internal set; } = 1;

    private static int _reportedConnected, _reportedLiving;

    /// <summary>One log line whenever the numbers change: what a test reads.</summary>
    internal static void Report()
    {
        if (!Coop.IsHost || !Coop.InRun || (Connected == _reportedConnected && Living == _reportedLiving)) return;
        _reportedConnected = Connected;
        _reportedLiving = Living;
        GD.Print($"[coop] scaling: {Connected} connected, {Living} living: hp x{Health(Connected):0.00} " +
                 $"boss x{BossHealth(Connected):0.00} spawn x{SpawnRate(Living):0.00} crowd x{Crowd(Living):0.00}");
    }

    /// <summary>A new run starts from one player.</summary>
    internal static void Reset()
    {
        Living = 1;
        _reportedConnected = _reportedLiving = 0;
    }
}
