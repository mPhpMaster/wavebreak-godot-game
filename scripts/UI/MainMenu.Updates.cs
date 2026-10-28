using Godot;
using Wavebreak.Meta;
using Wavebreak.Updates;

namespace Wavebreak.UI;

public sealed partial class MainMenu
{
    private UpdateCoordinator? _updates;
    private UpdatePanel? _updatePanel;
    private Button? _updateEntry;
    private Control? _updateHost;
    private static string? _notifiedRelease;
    private void AddUpdates(Control main)
    {
        _updates = UpdateRuntime.ForMenu();
        if (_updates is null) return;
        _updateHost = main;
        _updateEntry = MenuButton("GAME UPDATES", () => ShowPage("updates"));
        Place(_updateEntry, 470, 435, 520, 48); main.AddChild(_updateEntry);
        _pages["updates"] = _updatePanel = new UpdatePanel(_updates, () => ShowPage("main"));
        _updates.Changed += RefreshUpdateEntry;
        _updates.QuitRequested += QuitForUpdate;
        _ = _updates.StartupCheckAsync();
    }
    private void RefreshUpdateEntry()
    {
        if (!IsInsideTree()) return;
        Callable.From(() =>
        {
            if (_updateEntry is not null && IsInstanceValid(_updateEntry))
                _updateEntry.Text = Loc.T(_updates?.Snapshot.Release is { } release
                    && SemanticVersion.TryParse(_updates.Context.Version, out var current) && release.Version.CompareTo(current) > 0
                    ? "UPDATE AVAILABLE" : "GAME UPDATES");
            if (_updateHost is null || _updates?.Snapshot.Release is not { } available
                || !SemanticVersion.TryParse(_updates.Context.Version, out var installed)
                || available.Version.CompareTo(installed) <= 0 || _notifiedRelease == available.Tag) return;
            _notifiedRelease = available.Tag;
            var notice = Panel(470, 492, 520, 112);
            _updateHost.AddChild(notice);
            var rows = new VBoxContainer(); notice.AddChild(rows);
            var heading = new HBoxContainer(); rows.AddChild(heading);
            Label version = MakeLabel(Loc.F("Version {0} is available", available.Version.ToString()), 20, Gold);
            version.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; heading.AddChild(version);
            heading.AddChild(MenuButton("×", () => notice.QueueFree()));
            rows.AddChild(MenuButton("VIEW / DOWNLOAD UPDATE", () => ShowPage("updates")));
        }).CallDeferred();
    }
    private void QuitForUpdate() => Callable.From(() => GetTree().Quit()).CallDeferred();
    private void StopUpdates()
    {
        if (_updates is null) return;
        _updates.Changed -= RefreshUpdateEntry; _updates.QuitRequested -= QuitForUpdate; _updates.Cancel();
    }
}
