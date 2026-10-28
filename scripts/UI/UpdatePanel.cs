using System;
using Godot;
using Wavebreak.Meta;
using Wavebreak.Updates;

namespace Wavebreak.UI;

public sealed partial class UpdatePanel : PanelContainer
{
    private readonly UpdateCoordinator _updates;
    private readonly Action _back;
    private Label _status = null!, _detail = null!;
    private Button _install = null!, _check = null!, _cancel = null!, _backButton = null!;
    private ProgressBar _progress = null!;
    private CheckButton _restart = null!;
    private volatile bool _dirty = true;
    private readonly VBoxContainer _rows = new();

    public UpdatePanel(UpdateCoordinator updates, Action back) { _updates = updates; _back = back; }
    public override void _Ready()
    {
        MainMenu.Place(this, 44, 180, 960, 480);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(.035f, .045f, .075f, .96f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12 });
        AddChild(_rows);
        _rows.AddThemeConstantOverride("separation", 10);
        _rows.AddChild(MainMenu.MakeLabel("GAME UPDATES", 28, Colors.Gold));
        _rows.AddChild(_status = MainMenu.MakeLabel("", 22, Colors.White));
        _rows.AddChild(_detail = MainMenu.MakeLabel("", 17, Colors.LightGray));
        _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detail.CustomMinimumSize = new Vector2(0, 80);
        _rows.AddChild(_progress = new ProgressBar { CustomMinimumSize = new Vector2(0, 18) });
        _rows.AddChild(_restart = new CheckButton { Text = Loc.T("Restart after portable update"), ButtonPressed = true });
        _rows.AddChild(_install = MainMenu.MenuButton("DOWNLOAD & INSTALL", Install));
        _rows.AddChild(_check = MainMenu.MenuButton("CHECK FOR UPDATES", () => _ = _updates.CheckAsync()));
        var footer = new HBoxContainer(); _rows.AddChild(footer);
        footer.AddChild(_cancel = MainMenu.MenuButton("CANCEL", _updates.Cancel));
        footer.AddChild(MainMenu.MenuButton("RELEASES", () => OS.ShellOpen(UpdatePolicy.ReleasesUrl)));
        footer.AddChild(_backButton = MainMenu.MenuButton("BACK", () => { if (!_updates.Busy) _back(); }));
        _updates.Changed += OnChanged;
        Refresh();
    }
    private void OnChanged() => _dirty = true;
    public override void _ExitTree() { _updates.Changed -= OnChanged; _updates.Cancel(); }
    public override void _Process(double delta) { if (_dirty) Refresh(); }
    public void FocusFirst() => _check.GrabFocus();
    public bool CancelBusy() { if (!_updates.Busy) return false; _updates.Cancel(); return true; }
    private async void Install()
    {
        if (_updates.HasPrepared) await _updates.InstallAsync(_restart.ButtonPressed, Loc.IsRtl ? "ar" : "en");
        else await _updates.DownloadAsync(true, _restart.ButtonPressed, Loc.IsRtl ? "ar" : "en");
    }
    private void Refresh()
    {
        _dirty = false;
        UpdateSnapshot state = _updates.Snapshot;
        _status.Text = Loc.F("Installed: {0}  |  Latest: {1}", state.CurrentVersion, state.Release?.Version.ToString() ?? "—");
        string status = state.Phase switch
        {
            UpdatePhase.Checking => "Checking for updates…",
            UpdatePhase.Current => "You have the latest stable version.",
            UpdatePhase.Available => "A newer stable version is available.",
            UpdatePhase.Downloading => "Downloading and verifying…",
            UpdatePhase.Preparing => "Preparing update…",
            UpdatePhase.Ready => "Verified update is ready.",
            UpdatePhase.HandingOff => "Starting updater…",
            UpdatePhase.Cancelled => "Update cancelled. You can retry.",
            UpdatePhase.Failed => "Update failed. You can retry.",
            _ => "Check GitHub Releases for a newer stable version.",
        };
        _detail.Text = Loc.T(status) + "\n" + Loc.T(_updates.Context.CanInstall
            ? "DOWNLOAD & INSTALL closes the game after verification. Installed copies use the setup wizard; portable copies update in place."
            : "Automatic installation is unavailable for development or store-managed copies. Open RELEASES.");
        if (state.Problem != UpdateProblem.None) _detail.Text += "\n" + Loc.F("Update error: {0}", state.Problem.ToString());
        if (state.Detail.Length > 0) _detail.Text += "\n" + Loc.T(state.Detail);
        if (UpdateRuntime.RecoveryNote.Length > 0) _detail.Text += "\n" + Loc.T(UpdateRuntime.RecoveryNote);
        _progress.Value = state.TotalBytes > 0 ? 100d * state.CompletedBytes / state.TotalBytes : 0;
        _progress.Visible = state.Phase is UpdatePhase.Downloading or UpdatePhase.Preparing;
        _install.Disabled = _updates.Busy || !_updates.Context.CanInstall || state.Release is null
            || !SemanticVersion.TryParse(state.CurrentVersion, out var installed) || state.Release.Version.CompareTo(installed) <= 0;
        _install.Text = Loc.T(_updates.HasPrepared ? "INSTALL VERIFIED UPDATE" : "DOWNLOAD & INSTALL");
        _check.Disabled = _updates.Busy; _cancel.Disabled = !_updates.Busy;
        _backButton.Disabled = _updates.Busy; _restart.Disabled = _updates.Busy;
    }
}
