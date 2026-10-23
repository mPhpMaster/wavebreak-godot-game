using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Audio;

/// <summary>
/// Every sound in the game, played from the event bus. Nothing in gameplay holds
/// a reference to this node: it listens to the same events the HUD does, so it
/// can be removed — or a headless test can run without it — and nothing else
/// notices.
///
/// The hard part of audio in a survivors-like is not making sounds, it is not
/// making too many. Two hundred enemies dying in a second, three weapons firing
/// constantly and a magnet pulling in sixty gems would, played naively, be a
/// wall of noise and a few hundred node allocations a second. Three guards
/// keep that in check, cheapest first:
///
///   1. per-sound minimum interval — a sound cannot restart faster than it can
///      be heard as separate events;
///   2. per-sound voice cap — at most N copies of one sound at once;
///   3. a fixed voice pool — players are created once in <see cref="_Ready"/>;
///      when the pool is full a new sound steals the oldest voice of equal or
///      lower priority, or is dropped.
///
/// The whole node runs with <see cref="Node.ProcessModeEnum.Always"/>: the
/// level-up panel and the results screen pause the tree, and those are exactly
/// the moments the level-up chime and the run-end sting have to be heard.
///
/// It also owns the volume keys (see <see cref="AudioSettings"/>):
///   - / =   master down / up        9 / 0   effects down / up
///   [ / ]   music down / up         M       mute
/// with a short toast on screen, persisted to user://settings.cfg.
/// </summary>
public sealed partial class AudioDirector : Node
{
    // ------------------------------------------------------------- mixing

    private const int Voices2D = 12;
    private const int Voices3D = 16;

    private const string SfxBus = AudioSettings.SfxBus;
    private const string MusicBus = AudioSettings.MusicBus;

    /// <summary>The bed's own level under the Music bus. It is meant to be
    /// noticed only when it stops.</summary>
    private const float AmbientDb = -14f;

    /// <summary>How a sound is played. Volumes are relative to the normalised
    /// buffers (all synthesised to the same peak), so this table is the mix.</summary>
    private readonly struct Spec
    {
        public readonly float VolumeDb;
        public readonly float PitchJitter;   // ± fraction
        public readonly float MinInterval;   // seconds between starts
        public readonly int MaxVoices;
        public readonly bool Positional;
        public readonly int Priority;        // higher steals lower

        public Spec(float volumeDb, float pitchJitter, float minInterval, int maxVoices, bool positional, int priority)
        {
            VolumeDb = volumeDb; PitchJitter = pitchJitter; MinInterval = minInterval;
            MaxVoices = maxVoices; Positional = positional; Priority = priority;
        }
    }

    private static Spec SpecFor(Sfx id) => id switch
    {
        // Weapons fire constantly, so they sit far under everything else and are
        // thinned hardest — about two a second per shape however many weapons
        // and attack-speed items stack up. They are texture, not information;
        // the first mix played every shot and was called annoying for it.
        Sfx.FireProjectile => new(-30f, 0.08f, 0.45f, 1, false, 0),
        Sfx.FireMelee      => new(-27f, 0.06f, 0.45f, 1, false, 0),
        Sfx.FireAura       => new(-32f, 0.05f, 0.60f, 1, false, 0),
        Sfx.FireExplosive  => new(-26f, 0.06f, 0.45f, 1, false, 0),
        // Hits and deaths are positional: in a crowd, where things are dying is
        // useful, and distance attenuation thins the far side of the swarm. Hits
        // are capped near nine a second: enough to confirm contact, not a drumroll.
        Sfx.EnemyHit       => new(-24f, 0.10f, 0.11f, 2, true, 1),
        Sfx.EnemyDeath     => new(-17f, 0.08f, 0.08f, 3, true, 2),
        Sfx.EliteDeath     => new(-9f, 0.04f, 0.25f, 1, true, 4),
        // Everything about the player is non-positional: the camera trails the
        // player, so panning would only ever be wrong.
        Sfx.PlayerHurt     => new(-10f, 0.05f, 0.25f, 1, false, 5),
        Sfx.XpPickup       => new(-20f, 0.0f, 0.05f, 3, false, 1),
        Sfx.GoldPickup     => new(-18f, 0.0f, 0.08f, 2, false, 2),
        Sfx.LevelUp        => new(-8f, 0.0f, 0.3f, 1, false, 6),
        Sfx.Dash           => new(-15f, 0.05f, 0.1f, 1, false, 3),
        Sfx.Jump           => new(-18f, 0.05f, 0.08f, 1, false, 3),
        Sfx.AirJump        => new(-17f, 0.04f, 0.08f, 1, false, 3),
        Sfx.Slide          => new(-17f, 0.05f, 0.15f, 1, false, 3),
        Sfx.SlamStart      => new(-19f, 0.03f, 0.15f, 1, false, 3),
        Sfx.SlamImpact     => new(-9f, 0.04f, 0.15f, 1, false, 5),
        Sfx.Land           => new(-16f, 0.06f, 0.1f, 1, false, 2),
        Sfx.RunWin         => new(-8f, 0.0f, 1f, 1, false, 7),
        Sfx.RunLose        => new(-8f, 0.0f, 1f, 1, false, 7),
        _                  => new(-18f, 0.05f, 0.1f, 1, false, 1),
    };

    /// <summary>Pickup combo ladder in semitones (major pentatonic): quick
    /// successive pickups climb it, a pause resets it. Pentatonic so any two
    /// overlapping steps are consonant; one octave only, so a long magnet sweep
    /// tops out at a pleasant pitch instead of a whistle.</summary>
    private static readonly int[] ComboSteps = { 0, 2, 4, 7, 9, 12 };
    private const float ComboWindow = 0.6f;

    // ----------------------------------------------------------- buffers

    // Static: synthesis takes a few milliseconds and the result never changes,
    // so a restarted run reuses the streams instead of rebuilding them.
    private static AudioStreamWav[][]? _streams;
    private static AudioStreamWav? _ambientStream;

    private static readonly int SfxCount = Enum.GetValues<Sfx>().Length;

    // ------------------------------------------------------------- state

    private sealed class Voice
    {
        public Node Player = null!;
        public Sfx Id;
        public int Priority;
        public double StartedAt;
    }

    private readonly List<Voice> _voices2D = new(Voices2D);
    private readonly List<Voice> _voices3D = new(Voices3D);
    private readonly Spec[] _specs = new Spec[SfxCount];
    private readonly double[] _lastStart = new double[SfxCount];
    private readonly int[] _played = new int[SfxCount];
    private int _dropped;
    private int _stolen;

    private AudioStreamPlayer? _ambient;
    private uint _rand = 0x2545F491u;
    private int _combo;
    private double _lastPickup = -10.0;
    private bool _subscribed;

    /// <summary>The live director, for the end-of-run report. Null between runs.</summary>
    public static AudioDirector? Instance { get; private set; }

    // --------------------------------------------------------- lifecycle

    public override void _Ready()
    {
        Name = "Audio";
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;

        EnsureStreams();
        EnsureBuses();

        for (int i = 0; i < SfxCount; i++)
        {
            _specs[i] = SpecFor((Sfx)i);
            _lastStart[i] = -10.0;
        }

        for (int i = 0; i < Voices2D; i++)
        {
            var p = new AudioStreamPlayer { Bus = SfxBus };
            AddChild(p);
            _voices2D.Add(new Voice { Player = p });
        }
        for (int i = 0; i < Voices3D; i++)
        {
            var p = new AudioStreamPlayer3D
            {
                Bus = SfxBus,
                // Tuned for a camera that sits ~10 m behind the player: anything
                // on screen is near full level, the far edge of the arena is not.
                UnitSize = 14f,
                MaxDistance = 80f,
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
                AttenuationFilterDb = -6f,
                PanningStrength = 0.6f,
                DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,
            };
            AddChild(p);
            _voices3D.Add(new Voice { Player = p });
        }

        _ambient = new AudioStreamPlayer
        {
            Name = "Ambient",
            Bus = MusicBus,
            Stream = _ambientStream,
            VolumeDb = AmbientDb,
        };
        AddChild(_ambient);
        // Real music replaces the drone bed (its A drone clashes with other keys).
        if (!MusicDirector.Active) _ambient.Play();

        MaybeDump();
        MaybePressKeys();

        EventBus.Subscribe<WeaponFired>(OnWeaponFired);
        EventBus.Subscribe<EnemyDamaged>(OnEnemyDamaged);
        EventBus.Subscribe<EnemyKilled>(OnEnemyKilled);
        EventBus.Subscribe<PlayerDamaged>(OnPlayerDamaged);
        EventBus.Subscribe<XpGained>(OnXpGained);
        EventBus.Subscribe<GoldGained>(OnGoldGained);
        EventBus.Subscribe<LevelGained>(OnLevelGained);
        EventBus.Subscribe<PlayerMoved>(OnPlayerMoved);
        EventBus.Subscribe<RunEnded>(OnRunEnded);
        SubscribeChestSounds();
        SubscribeShrineSounds();
        _subscribed = true;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        if (!_subscribed) return;
        _subscribed = false;
        // Main resets the bus at run end anyway; unsubscribing here as well keeps
        // a director that is removed mid-run (a test, a settings toggle) from
        // being called after it is gone.
        EventBus.Unsubscribe<WeaponFired>(OnWeaponFired);
        EventBus.Unsubscribe<EnemyDamaged>(OnEnemyDamaged);
        EventBus.Unsubscribe<EnemyKilled>(OnEnemyKilled);
        EventBus.Unsubscribe<PlayerDamaged>(OnPlayerDamaged);
        EventBus.Unsubscribe<XpGained>(OnXpGained);
        EventBus.Unsubscribe<GoldGained>(OnGoldGained);
        EventBus.Unsubscribe<LevelGained>(OnLevelGained);
        EventBus.Unsubscribe<PlayerMoved>(OnPlayerMoved);
        EventBus.Unsubscribe<RunEnded>(OnRunEnded);
        UnsubscribeChestSounds();
        UnsubscribeShrineSounds();
    }

    private static void EnsureStreams()
    {
        if (_streams is not null) return;

        _streams = new AudioStreamWav[SfxCount][];
        for (int i = 0; i < SfxCount; i++)
        {
            var id = (Sfx)i;
            int n = SfxSynth.Variants(id);
            _streams[i] = new AudioStreamWav[n];
            for (int v = 0; v < n; v++)
                _streams[i][v] = SfxSynth.ToStream(SfxSynth.Build(id, v));
        }
        _ambientStream = SfxSynth.ToStream(SfxSynth.BuildAmbient(), loop: true);
    }

    /// <summary>Creates the Sfx and Music buses if this process has not already.
    /// Built in code because the project has no bus layout file, and checked by
    /// name because the AudioServer outlives a scene reload.</summary>
    internal static void EnsureBuses()
    {
        if (AudioServer.GetBusIndex(SfxBus) < 0)
        {
            AudioServer.AddBus();
            int i = AudioServer.BusCount - 1;
            AudioServer.SetBusName(i, SfxBus);
            AudioServer.SetBusSend(i, AudioSettings.MasterBus);
            // Catches the rare pile-up the per-sound guards let through.
            AudioServer.AddBusEffect(i, new AudioEffectHardLimiter { CeilingDb = -3f });
        }
        if (AudioServer.GetBusIndex(MusicBus) < 0)
        {
            AudioServer.AddBus();
            int i = AudioServer.BusCount - 1;
            AudioServer.SetBusName(i, MusicBus);
            AudioServer.SetBusSend(i, AudioSettings.MasterBus);
        }

        // Levels come from the player's saved preference, not from constants.
        AudioSettings.Load();
        AudioSettings.Apply();
    }

    // ----------------------------------------------------------- controls

    /// <summary>Key bindings, as (input action, channel, steps). A step of 0
    /// means the mute toggle.</summary>
    private static readonly (string Action, VolumeChannel Channel, int Steps)[] Controls =
    {
        ("volume_down", VolumeChannel.Master, -1),
        ("volume_up", VolumeChannel.Master, +1),
        ("sfx_volume_down", VolumeChannel.Sfx, -1),
        ("sfx_volume_up", VolumeChannel.Sfx, +1),
        ("music_volume_down", VolumeChannel.Music, -1),
        ("music_volume_up", VolumeChannel.Music, +1),
        ("mute", VolumeChannel.Master, 0),
    };

    private CanvasLayer? _toastLayer;
    private Label? _toast;
    private Tween? _toastTween;
    private bool _logControls;

    /// <summary>Unhandled, not _Input: a focused UI control (a level-up card, the
    /// restart button) gets first claim on a key. Runs while paused because the
    /// whole node is ProcessMode.Always — muting from the level-up screen works.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Echo: true }) return;
        foreach (var (action, channel, steps) in Controls)
        {
            if (!InputMap.HasAction(action) || !e.IsActionPressed(action)) continue;

            if (steps == 0) AudioSettings.ToggleMute();
            else AudioSettings.Nudge(channel, steps);

            ShowToast(AudioSettings.Describe(steps == 0 ? VolumeChannel.Master : channel));
            if (_logControls) GD.Print($"[audio] {action} -> {LevelsText()}");
            GetViewport().SetInputAsHandled();
            return;
        }
    }

    /// <summary>A brief line of text at the top of the screen. Owned here rather
    /// than by the HUD so volume feedback works on every screen, paused or not.</summary>
    private void ShowToast(string text)
    {
        if (_toast is null)
        {
            _toastLayer = new CanvasLayer { Name = "VolumeToast", Layer = 100 };
            AddChild(_toastLayer);
            _toast = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _toast.AddThemeFontSizeOverride("font_size", 22);
            _toast.AddThemeColorOverride("font_color", new Color(0.92f, 0.96f, 1f));
            _toast.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
            _toast.AddThemeConstantOverride("outline_size", 6);
            _toast.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
            _toast.OffsetLeft = -260f;
            _toast.OffsetRight = 260f;
            // Below the HUD's clock, clear of the level-up heading.
            _toast.OffsetTop = 112f;
            _toast.OffsetBottom = 146f;
            _toastLayer.AddChild(_toast);
        }

        _toast.Text = text;
        _toast.Modulate = Colors.White;
        _toastTween?.Kill();
        _toastTween = CreateTween();
        _toastTween.TweenInterval(1.1);
        _toastTween.TweenProperty(_toast, "modulate:a", 0f, 0.4);
    }

    private static string LevelsText()
    {
        int m = AudioServer.GetBusIndex(AudioSettings.MasterBus);
        return $"master={AudioSettings.Get(VolumeChannel.Master):0.0} " +
               $"sfx={AudioSettings.Get(VolumeChannel.Sfx):0.0} " +
               $"music={AudioSettings.Get(VolumeChannel.Music):0.0} " +
               $"muted={(AudioSettings.Muted ? 1 : 0)} " +
               $"masterdb={AudioServer.GetBusVolumeDb(m):0.0} " +
               $"busmute={(AudioServer.IsBusMute(m) ? 1 : 0)}";
    }

    /// <summary>
    /// <c>--audio-keys=volume_down,mute,...</c>: press these actions through the
    /// real input pipeline shortly after start and log each result. This is how
    /// a run without a keyboard proves the bindings reach the buses and that
    /// the setting persists into the next launch.
    /// </summary>
    private async void MaybePressKeys()
    {
        string? list = null;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--audio-keys=")) list = arg["--audio-keys=".Length..];
        if (list is null) return;

        _logControls = true;
        GD.Print($"[audio] before -> {LevelsText()}");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        foreach (string action in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
            Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    // ------------------------------------------------------------ events

    private void OnWeaponFired(WeaponFired e)
    {
        Sfx id = e.Shape switch
        {
            AttackShape.Melee => Sfx.FireMelee,
            AttackShape.Aura => Sfx.FireAura,
            AttackShape.Explosive => Sfx.FireExplosive,
            _ => Sfx.FireProjectile,
        };
        Play(id);
    }

    private void OnEnemyDamaged(EnemyDamaged e)
    {
        if (e.Info.Nullified) return;
        // Pooled enemies are recycled rather than freed, but a target can still
        // be a node on its way out; touching its position would throw.
        if (e.Target is GodotObject go && !IsInstanceValid(go)) return;
        // A crit is the same tick a fifth higher: readable without a new sound.
        Play(Sfx.EnemyHit, e.Target.Position, pitch: e.Info.Crit ? 1.25f : 1f);
    }

    private void OnEnemyKilled(EnemyKilled e)
        => Play(e.WasElite ? Sfx.EliteDeath : Sfx.EnemyDeath, e.Position);

    private void OnPlayerDamaged(PlayerDamaged e)
    {
        if (e.Info.Nullified || e.Info.Amount <= 0f) return;
        Play(Sfx.PlayerHurt);
    }

    private void OnXpGained(XpGained e)
    {
        if (e.Amount > 0) Play(Sfx.XpPickup, pitch: ComboPitch());
    }

    private void OnGoldGained(GoldGained e)
    {
        // Negative amounts are spending, which is not a pickup.
        if (e.Amount > 0) Play(Sfx.GoldPickup, pitch: ComboPitch());
    }

    private void OnLevelGained(LevelGained e) => Play(Sfx.LevelUp);

    private void OnRunEnded(RunEnded e)
    {
        Play(e.Survived ? Sfx.RunWin : Sfx.RunLose);
        // Duck the bed under the sting and leave it low behind the results.
        if (_ambient is not null) _ambient.VolumeDb = AmbientDb - 8f;
    }

    private void OnPlayerMoved(PlayerMoved e)
    {
        switch (e.Verb)
        {
            case MoveVerb.Jump: Play(Sfx.Jump); break;
            case MoveVerb.AirJump: Play(Sfx.AirJump); break;
            case MoveVerb.Dash: Play(Sfx.Dash); break;
            case MoveVerb.Slide: Play(Sfx.Slide); break;
            case MoveVerb.SlamStart: Play(Sfx.SlamStart); break;
            case MoveVerb.SlamImpact:
                // Bigger crowd, deeper boom: pitch down rather than louder, since
                // the slam is already one of the loudest sounds in the mix.
                Play(Sfx.SlamImpact, pitch: 1f - 0.15f * e.Strength);
                break;
            case MoveVerb.Land:
                // Only real drops: a short hop's landing is felt, not heard.
                if (e.Strength < 0.3f) break;
                Play(Sfx.Land, gainDb: 20f * MathF.Log10(0.35f + 0.65f * e.Strength));
                break;
        }
    }

    /// <summary>Climbs the pickup ladder on quick successive pickups. Advances
    /// even when the sound itself is throttled, so a magnet sweep still rises
    /// at the rate gems arrive rather than the rate they are heard.</summary>
    private float ComboPitch()
    {
        double now = Now();
        _combo = now - _lastPickup < ComboWindow ? Math.Min(_combo + 1, ComboSteps.Length - 1) : 0;
        _lastPickup = now;
        return MathF.Pow(2f, ComboSteps[_combo] / 12f);
    }

    // ------------------------------------------------------------ voices

    private void Play(Sfx id, float pitch = 1f, float gainDb = 0f)
        => Play(id, Vector3.Zero, positional: false, pitch, gainDb);

    private void Play(Sfx id, Vector3 at, float pitch = 1f, float gainDb = 0f)
        => Play(id, at, positional: true, pitch, gainDb);

    private void Play(Sfx id, Vector3 at, bool positional, float pitch, float gainDb)
    {
        if (_streams is null) return;
        int k = (int)id;
        Spec spec = _specs[k];
        double now = Now();

        if (now - _lastStart[k] < spec.MinInterval) { _dropped++; return; }

        bool use3D = positional && spec.Positional;
        List<Voice> pool = use3D ? _voices3D : _voices2D;

        Voice? free = null, oldestSame = null, victim = null;
        int sameCount = 0;
        foreach (Voice v in pool)
        {
            bool busy = IsPlaying(v.Player);
            if (!busy) { free ??= v; continue; }
            if (v.Id == id)
            {
                sameCount++;
                if (oldestSame is null || v.StartedAt < oldestSame.StartedAt) oldestSame = v;
            }
            if (v.Priority <= spec.Priority
                && (victim is null || v.Priority < victim.Priority
                    || (v.Priority == victim.Priority && v.StartedAt < victim.StartedAt)))
                victim = v;
        }

        Voice? voice;
        if (sameCount >= spec.MaxVoices)
        {
            // At the cap, the newest event wins over the oldest copy of itself:
            // a fresh hit is more informative than the tail of an old one.
            voice = oldestSame;
            _stolen++;
        }
        else if (free is not null) voice = free;
        else if (victim is not null) { voice = victim; _stolen++; }
        else { _dropped++; return; }

        if (voice is null) { _dropped++; return; }

        AudioStreamWav[] takes = _streams[k];
        AudioStreamWav stream = takes.Length == 1 ? takes[0] : takes[(int)(NextRand() * takes.Length) % takes.Length];
        float jitter = 1f + (NextRand() * 2f - 1f) * spec.PitchJitter;
        float finalPitch = Math.Clamp(pitch * jitter, 0.25f, 4f);
        float volume = spec.VolumeDb + gainDb;

        switch (voice.Player)
        {
            case AudioStreamPlayer3D p3:
                p3.Stream = stream;
                p3.PitchScale = finalPitch;
                p3.VolumeDb = volume;
                p3.GlobalPosition = at;
                p3.Play();
                break;
            case AudioStreamPlayer p2:
                p2.Stream = stream;
                p2.PitchScale = finalPitch;
                p2.VolumeDb = volume;
                p2.Play();
                break;
        }

        voice.Id = id;
        voice.Priority = spec.Priority;
        voice.StartedAt = now;
        _lastStart[k] = now;
        _played[k]++;
    }

    private static bool IsPlaying(Node player) => player switch
    {
        AudioStreamPlayer3D p3 => p3.Playing,
        AudioStreamPlayer p2 => p2.Playing,
        _ => false,
    };

    /// <summary>Wall-clock seconds. Not the scene's process delta: rate limits
    /// must keep working while the tree is paused.</summary>
    private static double Now() => Time.GetTicksUsec() / 1_000_000.0;

    /// <summary>Own xorshift in [0, 1), for pitch jitter and take selection.
    /// Never the run's RNG — that would make seeded gameplay depend on audio.</summary>
    private float NextRand()
    {
        _rand ^= _rand << 13;
        _rand ^= _rand >> 17;
        _rand ^= _rand << 5;
        return (_rand >> 8) / 16777216f;
    }

    // ------------------------------------------------------------ report

    /// <summary>
    /// One machine-readable line of what was heard, printed next to SUMMARY by
    /// verification runs. Separate from SUMMARY on purpose: tests parse that
    /// line, and audio is not something they should have to know about.
    /// </summary>
    public string Report()
    {
        int P(Sfx id) => _played[(int)id];
        int total = 0;
        foreach (int n in _played) total += n;

        var sb = new StringBuilder("AUDIO");
        sb.Append(" played=").Append(total);
        sb.Append(" fire=").Append(P(Sfx.FireProjectile) + P(Sfx.FireMelee) + P(Sfx.FireAura) + P(Sfx.FireExplosive));
        sb.Append(" hit=").Append(P(Sfx.EnemyHit));
        sb.Append(" death=").Append(P(Sfx.EnemyDeath) + P(Sfx.EliteDeath));
        sb.Append(" hurt=").Append(P(Sfx.PlayerHurt));
        sb.Append(" xp=").Append(P(Sfx.XpPickup));
        sb.Append(" gold=").Append(P(Sfx.GoldPickup));
        sb.Append(" levelup=").Append(P(Sfx.LevelUp));
        sb.Append(" dash=").Append(P(Sfx.Dash));
        sb.Append(" jump=").Append(P(Sfx.Jump));
        sb.Append(" airjump=").Append(P(Sfx.AirJump));
        sb.Append(" slide=").Append(P(Sfx.Slide));
        sb.Append(" slam=").Append(P(Sfx.SlamImpact));
        sb.Append(" land=").Append(P(Sfx.Land));
        sb.Append(" runend=").Append(P(Sfx.RunWin) + P(Sfx.RunLose));
        sb.Append(" chest=").Append(_chestPlayed);
        sb.Append(" dropped=").Append(_dropped);
        sb.Append(" stolen=").Append(_stolen);
        sb.Append(" ambient=").Append(_ambient is not null && _ambient.Playing ? 1 : 0);
        sb.Append(" mixrate=").Append((int)AudioServer.GetMixRate());
        sb.Append(' ').Append(LevelsText());
        return sb.ToString();
    }

    // -------------------------------------------------------------- dump

    /// <summary>
    /// <c>--dump-sfx=&lt;dir&gt;</c>: write every synthesised buffer as a .wav and
    /// print its length and peak, so the sounds can be checked without
    /// listening — non-silent, not clipping, a sensible length.
    /// </summary>
    private static void MaybeDump()
    {
        string? dir = null;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--dump-sfx=")) dir = arg["--dump-sfx=".Length..];
        if (dir is null || _streams is null) return;

        DirAccess.MakeDirRecursiveAbsolute(dir);
        for (int i = 0; i < SfxCount; i++)
        {
            float[] samples = SfxSynth.Build((Sfx)i, 0);
            DumpOne(dir, ((Sfx)i).ToString(), samples, _streams[i][0]);
        }
        DumpOne(dir, "Ambient", SfxSynth.BuildAmbient(), _ambientStream!);
    }

    private static void DumpOne(string dir, string name, float[] samples, AudioStreamWav stream)
    {
        float peak = 0f;
        double sum = 0;
        foreach (float s in samples)
        {
            peak = MathF.Max(peak, MathF.Abs(s));
            sum += s * s;
        }
        float rms = (float)Math.Sqrt(sum / Math.Max(1, samples.Length));
        // Edge values: a buffer that starts or ends far from zero clicks.
        float first = MathF.Abs(samples[0]);
        float last = MathF.Abs(samples[^1]);
        string path = $"{dir}/{name}.wav";
        Error err = stream.SaveToWav(path);
        GD.Print($"[audio] {name,-15} {samples.Length / (float)SfxSynth.Rate:0.000}s peak={peak:0.000} " +
                 $"rms={rms:0.000} first={first:0.0000} last={last:0.0000} -> {err}");
    }
}
