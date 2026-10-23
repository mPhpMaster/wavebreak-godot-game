using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Meta;

namespace Wavebreak.Audio;

/// <summary>
/// The music: one looping track at a time, crossfaded on the run's big moments.
///
///   title screen          -> title
///   PhaseStarted          -> the map's track (StageDef.MusicId when present,
///                            otherwise matched from the stage id / theme)
///   PhaseBossArrived      -> boss
///   PortalOpened          -> back to the map's track
///   FinalBossDefeated     -> victory sting, then swarm (if a swarm follows)
///   RunEnded              -> victory or defeat sting, then silence
///
/// Tracks are rendered offline by <c>tools/compose_music.py</c> into
/// <c>assets/music/*.ogg</c> and are exactly periodic, so Godot's own loop is
/// seamless. Everything plays on the Music bus, so the music slider, the
/// [ / ] keys and mute all apply.
///
/// Lifetime: one node under the tree root, created on first use. It outlives
/// scene changes, which is what lets the title theme crossfade into the first
/// map instead of cutting. It does nothing per frame: two stream players, a
/// sting player, tweens while a fade is running, and a 4 Hz timer that ducks
/// the bus while the tree is paused (level-up, pause menu, results).
///
/// Automated runs (<see cref="Launch.Automated"/>) never create it, so tests,
/// captures and the AUDIO report are exactly as before.
/// </summary>
public sealed partial class MusicDirector : Node
{
    private const string Dir = "res://assets/music/";

    /// <summary>Loop level under the Music bus. Tracks are mastered near
    /// -20 dBFS RMS, so this keeps music well under the effects.</summary>
    private const float LoopDb = -9f;
    private const float StingDb = -6f;
    private const float SilentDb = -60f;
    private const float PausedDuckDb = -5f;

    private static readonly string[] MapTracks = { "meadow", "frost", "ashlands", "graveyard", "tidal" };

    private static readonly Dictionary<string, AudioStream?> Cache = new();

    public static MusicDirector? Instance { get; private set; }

    /// <summary>True when music will play: an interactive launch with the
    /// tracks present. The ambient bed steps aside when this is set.</summary>
    public static bool Active
    {
        get
        {
            Launch.Parse();
            return !Launch.Automated && Load("title") is not null;
        }
    }

    private AudioStreamPlayer _a = null!, _b = null!, _sting = null!;
    private AudioStreamPlayer _current = null!;
    private string? _currentTrack;
    private string? _stageTrack;
    private Tween? _fadeIn, _fadeOut;
    private AudioEffectAmplify? _duck;
    private bool _ducked;
    private bool _swarm;
    private bool _subscribed;
    private string? _pending;
    private SceneTreeTimer? _afterSting;

    // ------------------------------------------------------------- entry points

    /// <summary>Title screen: start (or crossfade to) the title theme.</summary>
    public static void PlayTitle(Node from) => Ensure(from)?.Request("title", 2.0f);

    /// <summary>Run scene: listen to this run's events. Called after the run's
    /// bus is fresh; the subscription is dropped by EventBus.Reset at run end
    /// and taken again here next run.</summary>
    public static void BeginRun(Node from)
    {
        MusicDirector? m = Ensure(from);
        if (m is null) return;
        m._swarm = false;
        m.Subscribe();
    }

    private static MusicDirector? Ensure(Node from)
    {
        if (!Active) return null;
        if (Instance is not null && IsInstanceValid(Instance)) return Instance;
        Instance = new MusicDirector { Name = "Music" };
        // Deferred: the root is usually busy setting up the scene that called us.
        from.GetTree().Root.CallDeferred(Node.MethodName.AddChild, Instance);
        return Instance;
    }

    // ---------------------------------------------------------------- lifecycle

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        AudioDirector.EnsureBuses();

        _a = NewPlayer("A");
        _b = NewPlayer("B");
        _sting = NewPlayer("Sting");
        _current = _a;

        int bus = AudioServer.GetBusIndex(AudioSettings.MusicBus);
        if (bus >= 0)
        {
            for (int i = 0; i < AudioServer.GetBusEffectCount(bus); i++)
                if (AudioServer.GetBusEffect(bus, i) is AudioEffectAmplify amp) _duck = amp;
            if (_duck is null)
            {
                _duck = new AudioEffectAmplify { VolumeDb = 0f };
                AudioServer.AddBusEffect(bus, _duck);
            }
        }

        var timer = new Timer { WaitTime = 0.25, Autostart = true, ProcessCallback = Timer.TimerProcessCallback.Idle };
        timer.Timeout += CheckPause;
        AddChild(timer);

        if (_pending is not null) Crossfade(_pending, 2.0f);
        _pending = null;
    }

    public override void _ExitTree()
    {
        Unsubscribe();
        if (Instance == this) Instance = null;
    }

    private AudioStreamPlayer NewPlayer(string name)
    {
        var p = new AudioStreamPlayer { Name = name, Bus = AudioSettings.MusicBus, VolumeDb = SilentDb };
        AddChild(p);
        return p;
    }

    private void Subscribe()
    {
        // Unsubscribe first: if the bus was not reset, this keeps it to one copy.
        Unsubscribe();
        EventBus.Subscribe<PhaseStarted>(OnPhaseStarted);
        EventBus.Subscribe<PhaseBossArrived>(OnBoss);
        EventBus.Subscribe<PortalOpened>(OnPortal);
        EventBus.Subscribe<FinalBossDefeated>(OnFinalBoss);
        EventBus.Subscribe<RunEnded>(OnRunEnded);
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        EventBus.Unsubscribe<PhaseStarted>(OnPhaseStarted);
        EventBus.Unsubscribe<PhaseBossArrived>(OnBoss);
        EventBus.Unsubscribe<PortalOpened>(OnPortal);
        EventBus.Unsubscribe<FinalBossDefeated>(OnFinalBoss);
        EventBus.Unsubscribe<RunEnded>(OnRunEnded);
    }

    // ------------------------------------------------------------------ events

    private void OnPhaseStarted(PhaseStarted e)
    {
        _stageTrack = TrackForStage(e.StageId, e.Phase);
        Request(_stageTrack, 2.5f);
    }

    private void OnBoss(PhaseBossArrived e) => Request("boss", 1.5f);

    private void OnPortal(PortalOpened e) => Request(_stageTrack ?? "meadow", 3.0f);

    private void OnFinalBoss(FinalBossDefeated e)
    {
        // Without a swarm the run ends a few seconds later and RunEnded stings.
        if (!e.SwarmFollows) { FadeOutLoop(1.5f); return; }
        _swarm = true;
        PlaySting("victory", then: "swarm", after: 3.2f);
    }

    private void OnRunEnded(RunEnded e)
        => PlaySting(e.Survived && !_swarm ? "victory" : "defeat", then: null, after: 0f);

    // --------------------------------------------------------------- mechanics

    private void Request(string track, float seconds)
    {
        if (!IsInsideTree()) { _pending = track; return; }
        Crossfade(track, seconds);
    }

    private void Crossfade(string track, float seconds)
    {
        if (track == _currentTrack && _current.Playing) return;
        AudioStream? stream = Load(track);
        if (stream is null) return;

        AudioStreamPlayer from = _current, to = from == _a ? _b : _a;
        _fadeIn?.Kill();
        _fadeOut?.Kill();

        to.Stream = stream;
        to.VolumeDb = SilentDb;
        to.Play();
        _fadeIn = CreateTween();
        _fadeIn.TweenProperty(to, "volume_db", LoopDb, seconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);

        if (from.Playing)
        {
            _fadeOut = CreateTween();
            _fadeOut.TweenProperty(from, "volume_db", SilentDb, seconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
            _fadeOut.TweenCallback(Callable.From(from.Stop));
        }

        _current = to;
        _currentTrack = track;
    }

    private void FadeOutLoop(float seconds)
    {
        _fadeIn?.Kill();
        _fadeOut?.Kill();
        foreach (AudioStreamPlayer p in new[] { _a, _b })
        {
            if (!p.Playing) continue;
            Tween t = CreateTween();
            t.TweenProperty(p, "volume_db", SilentDb, seconds);
            t.TweenCallback(Callable.From(p.Stop));
            _fadeOut = t;
        }
        _currentTrack = null;
    }

    private void PlaySting(string sting, string? then, float after)
    {
        if (!IsInsideTree()) return;
        FadeOutLoop(0.6f);
        AudioStream? s = Load(sting);
        if (s is not null)
        {
            _sting.Stream = s;
            _sting.VolumeDb = StingDb;
            _sting.Play();
        }
        if (then is null) return;
        _afterSting = GetTree().CreateTimer(after, processAlways: true);
        string next = then;
        _afterSting.Timeout += () => { if (IsInsideTree()) Crossfade(next, 2.0f); };
    }

    /// <summary>Duck the whole Music bus a little while the tree is paused.
    /// Polled at 4 Hz rather than hooked into each panel, so no gameplay file
    /// has to know music exists.</summary>
    private void CheckPause()
    {
        if (_duck is null || !IsInsideTree()) return;
        bool paused = GetTree().Paused;
        if (paused == _ducked) return;
        _ducked = paused;
        Tween t = CreateTween();
        t.TweenProperty(_duck, "volume_db", paused ? PausedDuckDb : 0f, 0.4);
    }

    // ------------------------------------------------------------------ tracks

    /// <summary>
    /// Which loop a stage plays. A <c>MusicId</c> on the StageDef wins (read by
    /// reflection so this compiles before that field exists); it may be a track
    /// name ("frost") or a res:// path. Otherwise the id and theme are matched
    /// by keyword, and an unknown map gets a map track by phase.
    /// </summary>
    public static string TrackForStage(string stageId, int phase)
    {
        string theme = "";
        foreach (StageDef s in Stages.Run)
        {
            if (s.Id != stageId) continue;
            theme = s.Theme ?? "";
            if (s.GetType().GetProperty("MusicId")?.GetValue(s) is string mid && mid.Length > 0
                && Load(mid) is not null)
                return mid;
            break;
        }

        string key = (stageId + " " + theme).ToLowerInvariant();
        if (Has(key, "meadow", "thorn", "grass", "field")) return "meadow";
        if (Has(key, "frost", "rime", "ice", "snow", "glass")) return "frost";
        if (Has(key, "ash", "cinder", "ember", "lava", "fire")) return "ashlands";
        if (Has(key, "grave", "crypt", "tomb", "bone", "cemetery")) return "graveyard";
        if (Has(key, "tid", "sea", "reef", "shore", "coast", "ocean", "wave")) return "tidal";
        return MapTracks[Math.Max(0, phase - 1) % MapTracks.Length];
    }

    private static bool Has(string s, params string[] words)
    {
        foreach (string w in words) if (s.Contains(w)) return true;
        return false;
    }

    /// <summary>Loads a track by name or path. Uses the imported resource when
    /// there is one, and otherwise reads the .ogg directly, so a fresh checkout
    /// has music before the editor has imported anything.</summary>
    private static AudioStream? Load(string track)
    {
        if (Cache.TryGetValue(track, out AudioStream? cached)) return cached;
        string path = track.StartsWith("res://") ? track : Dir + track + ".ogg";
        AudioStream? stream = null;
        if (ResourceLoader.Exists(path))
            stream = ResourceLoader.Load<AudioStream>(path);
        else if (FileAccess.FileExists(path))
            stream = AudioStreamOggVorbis.LoadFromFile(ProjectSettings.GlobalizePath(path));

        bool loop = track is not ("victory" or "defeat");
        if (stream is AudioStreamOggVorbis ogg) ogg.Loop = loop;
        Cache[track] = stream;
        return stream;
    }
}
