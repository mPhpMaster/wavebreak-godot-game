using System.Collections.Generic;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Core;
using Wavebreak.Game.Trade;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.UI;

/// <summary>
/// The swap meet's windows, on their own layer in the rest room (it leaves
/// with the room): the teammate picker, the "wants to trade" invite and the
/// trade window itself (<see cref="TradePanel"/>), plus the stall's little
/// sounds. Everything it shows comes from <see cref="TradeView"/>; every
/// button goes through <see cref="NetSession"/>.
/// </summary>
public sealed partial class TradeUi : Control
{
    public static TradeUi? Instance { get; private set; }

    private TradePickPanel _picker = null!;
    private TradeInvitePanel _invite = null!;
    private TradePanel _trade = null!;
    private AudioStreamPlayer _sfx = null!;
    private static AudioStreamWav[]? _sounds;
    private bool _wasOpen;
    private int _lastVersion;
    private string _lastTheirs = "";
    private int _readyCount;

    public bool TradeShown => _trade.IsOpen;

    public override void _Ready()
    {
        Name = "TradeUi";
        Instance = this;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always;

        _picker = new TradePickPanel();
        AddChild(_picker);
        _invite = new TradeInvitePanel();
        AddChild(_invite);
        _trade = new TradePanel();
        AddChild(_trade);

        if (_sounds is null)
        {
            _sounds = new AudioStreamWav[5];
            for (int i = 0; i < _sounds.Length; i++)
                _sounds[i] = SfxSynth.ToStream(SfxSynth.BuildTrade((SfxSynth.TradeSound)i));
        }
        _sfx = new AudioStreamPlayer { Bus = AudioSettings.SfxBus, VolumeDb = -7f };
        AddChild(_sfx);

        TradeView.Changed += OnChanged;
        TradeView.Ended += OnEnded;
        TradeView.Invited += OnInvited;
        if (TradeView.InviteId != 0) OnInvited(TradeView.InviteId, TradeView.InviteFrom);
        if (TradeView.Open) OnChanged();
    }

    public override void _ExitTree()
    {
        TradeView.Changed -= OnChanged;
        TradeView.Ended -= OnEnded;
        TradeView.Invited -= OnInvited;
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    public void ShowPicker()
    {
        if (TradeView.Open) { ShowTrade(); return; }
        _picker.Open();
    }

    public void ShowTrade()
    {
        if (!TradeView.Open) return;
        _picker.Close();
        _invite.Close();
        if (!_trade.IsOpen) _trade.Open();
        _trade.Refresh();
    }

    internal void Play(SfxSynth.TradeSound s)
    {
        if (_sounds is null || !IsInsideTree()) return;
        _sfx.Stream = _sounds[(int)s];
        _sfx.Play();
    }

    private void OnInvited(int askId, string from)
    {
        if (TradeView.Open) return;
        _picker.Close();
        _invite.Open(from);
        Play(SfxSynth.TradeSound.Invite);
    }

    private void OnChanged()
    {
        if (!IsInsideTree()) return;
        if (_invite.IsOpen && TradeView.InviteId == 0) _invite.Close();
        if (!TradeView.Open) { _wasOpen = false; return; }

        if (!_wasOpen)
        {
            _wasOpen = true;
            _lastVersion = TradeView.Version;
            _lastTheirs = "";
            _readyCount = 0;
            ShowTrade();
            Play(SfxSynth.TradeSound.Accept);
        }
        else
        {
            string theirs = TradeView.Theirs.Pack();
            if (theirs != _lastTheirs && _lastTheirs.Length > 0) Play(SfxSynth.TradeSound.Offer);
            int ready = (TradeView.Ready[0] ? 1 : 0) + (TradeView.Ready[1] ? 1 : 0) + (TradeView.Confirm[0] ? 1 : 0) + (TradeView.Confirm[1] ? 1 : 0);
            if (ready > _readyCount) Play(SfxSynth.TradeSound.Accept);
            _readyCount = ready;
            _lastTheirs = theirs;
        }
        _lastTheirs = TradeView.Theirs.Pack();
        _lastVersion = TradeView.Version;
        if (_trade.IsOpen) _trade.Refresh();
    }

    private void OnEnded(bool completed, string reason, string arg)
    {
        _wasOpen = false;
        _trade.CloseQuietly();
        Play(completed ? SfxSynth.TradeSound.Complete : SfxSynth.TradeSound.Cancel);
        string text = completed ? Loc.T("Trade complete!") : Loc.F(reason, arg);
        Color c = completed ? new Color(0.55f, 1f, 0.7f) : new Color(1f, 0.75f, 0.55f);
        if (Actors.Player.Instance is { } me && IsInstanceValid(me))
            EventBus.Publish(new PopupRequested(me.GlobalPosition + Vector3.Up * 3.2f, text, c, 1.8f));
    }
}

/// <summary>"Trade with whom?": one button per teammate (1-3), Esc to leave.</summary>
public sealed partial class TradePickPanel : ModalPanel
{
    private VBoxContainer _list = null!;
    private readonly List<long> _ids = new();

    public override void _Ready()
    {
        Name = "TradePickPanel";
        base._Ready();
        TradePanel.CuteFrame(Body);
        Body.CustomMinimumSize = new Vector2(440f, 0f);
        Body.AddChild(CuteStyle.Title(Loc.T("SWAP MEET"), 32, Actors.Trader.TraderTint, HorizontalAlignment.Center));
        Body.AddChild(CuteStyle.Text(Loc.T("Trade with whom?"), 17, CuteStyle.InkSoft, HorizontalAlignment.Center));
        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 8);
        Body.AddChild(_list);
        Body.AddChild(CuteStyle.Text(Loc.T("press  1 · 2 · 3  to pick      Esc to leave"), 13, CuteStyle.InkSoft, HorizontalAlignment.Center));
    }

    public void Open()
    {
        foreach (Node n in _list.GetChildren()) n.QueueFree();
        _ids.Clear();
        if (NetSession.Instance is { } net)
            foreach (CoopPeer p in net.TradePartners())
            {
                long id = p.Id;
                int k = _ids.Count + 1;
                _ids.Add(id);
                Button b = CuteStyle.Pill(Loc.F("{0}   {1}", k, p.Name), Coop.SlotColor(p.Slot), () => Pick(id), 19);
                b.CustomMinimumSize = new Vector2(0f, 44f);
                _list.AddChild(b);
            }
        if (_ids.Count == 0)
            _list.AddChild(CuteStyle.Text(Loc.T("Trading needs another player"), 17, CuteStyle.Ink, HorizontalAlignment.Center));
        OpenPanel();
    }

    protected override bool OnNumber(int index)
    {
        if (index >= _ids.Count) return false;
        Pick(_ids[index]);
        return true;
    }

    private void Pick(long id)
    {
        if (!Armed) return;
        Close();
        NetSession.Instance?.TradeRequest(id);
    }
}

/// <summary>"Bob wants to trade with you": ACCEPT (1) or DECLINE (2 / Esc).
/// Closing it any other way declines.</summary>
public sealed partial class TradeInvitePanel : ModalPanel
{
    private Label _who = null!;
    private bool _answered;

    public override void _Ready()
    {
        Name = "TradeInvitePanel";
        base._Ready();
        TradePanel.CuteFrame(Body);
        Body.CustomMinimumSize = new Vector2(460f, 0f);
        Body.AddChild(CuteStyle.Title(Loc.T("SWAP MEET"), 32, Actors.Trader.TraderTint, HorizontalAlignment.Center));
        _who = CuteStyle.Text("", 20, CuteStyle.Ink, HorizontalAlignment.Center);
        _who.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Body.AddChild(_who);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 14);
        Body.AddChild(row);
        Button yes = CuteStyle.Pill(Loc.T("1  ACCEPT"), CuteStyle.Mint, () => Answer(true), 19);
        Button no = CuteStyle.Pill(Loc.T("2  DECLINE"), CuteStyle.Coral, () => Answer(false), 19);
        yes.CustomMinimumSize = no.CustomMinimumSize = new Vector2(170f, 46f);
        row.AddChild(yes);
        row.AddChild(no);
    }

    public void Open(string from)
    {
        _answered = false;
        _who.Text = Loc.F("{0} wants to trade with you.", from);
        OpenPanel();
    }

    protected override bool OnNumber(int index)
    {
        if (index > 1) return false;
        Answer(index == 0);
        return true;
    }

    private void Answer(bool yes)
    {
        if (!Armed) return;
        _answered = true;
        Close();
        NetSession.Instance?.TradeRespond(yes);
    }

    protected override void OnClosed()
    {
        // Esc, E, or the room ending: a no, unless the invite is already gone.
        if (!_answered && TradeView.InviteId != 0) NetSession.Instance?.TradeRespond(false);
        _answered = false;
    }
}
