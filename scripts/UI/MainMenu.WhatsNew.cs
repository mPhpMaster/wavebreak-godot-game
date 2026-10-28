using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

public sealed partial class MainMenu
{
    private WhatsNewPanel? _whatsNew;
    private void AddWhatsNew()
    {
        var entry = MenuButton("WHAT'S NEW", OpenWhatsNew);
        Place(entry, 470, 610, 520, 44);
        _pages["main"].AddChild(entry);
    }

    private void OfferWhatsNew()
    {
        if (Launch.MenuPage == "whats-new" || (!Launch.Automated && _page == "main" && ReleaseNotes.Unread(ReleaseNotes.CurrentVersion)))
            OpenWhatsNew();
    }

    private void OpenWhatsNew()
    {
        if (_whatsNew is not null || ReleaseNotes.LoadCurrent() is not { } notes) return;
        Control? previousFocus = GetViewport().GuiGetFocusOwner();
        var layer = new CanvasLayer { Layer = 40 };
        AddChild(layer);
        _whatsNew = new WhatsNewPanel(notes, () =>
        {
            ReleaseNotes.Acknowledge(notes.Version);
            _whatsNew = null;
            layer.QueueFree();
            if (IsInstanceValid(previousFocus) && previousFocus!.IsVisibleInTree()) previousFocus.GrabFocus();
        });
        layer.AddChild(_whatsNew);
    }
}
