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
        // The newest thing to try, over the button that tells the rest.
        Label fresh = MakeLabel("NEW: Puzzle route. Switch it on beside the route on the PLAY page.", 15, Gold);
        fresh.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        fresh.VerticalAlignment = VerticalAlignment.Bottom;
        Place(fresh, 470, 556, 520, 48);
        _pages["main"].AddChild(fresh);
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
