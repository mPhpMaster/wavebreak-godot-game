using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// Character page additions: the scrolling roster and the hat picker that sits
/// under the skin picker. Hats are chosen per character, previewed on the
/// backdrop hero even when locked, remembered only when owned, and Pearl-priced
/// ones can be bought on the spot.
/// </summary>
public sealed partial class MainMenu
{
    private Label _hatLabel = null!;
    private Button _hatBuy = null!;
    private int _hatIndex;
    private CharacterDef? _hatCharacter;

    private ScrollContainer? _charScroll;

    private VBoxContainer CharacterScroll(VBoxContainer column)
    {
        var scroll = _charScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(340f, 320f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        var inner = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        inner.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(inner);
        column.AddChild(scroll);
        return inner;
    }

    /// <summary>Bring a roster button into view. FollowFocus does this for focus
    /// moved by the player, but not for focus given while the page is first
    /// laid out (the scroll has no size yet), so it is asked again next frame.</summary>
    private void ScrollRosterTo(Button b)
    {
        if (_charScroll is null) return;
        Callable.From(() =>
        {
            if (IsInstanceValid(_charScroll) && IsInstanceValid(b)) _charScroll.EnsureControlVisible(b);
        }).CallDeferred();
    }

    private void BuildHatRow(VBoxContainer box)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        Button prev = HatArrow("<", -1);
        Button next = HatArrow(">", +1);
        _hatLabel = MakeLabel("", 18, Ink);
        _hatLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _hatLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _hatLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hatBuy = new Button { Text = "BUY", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(96f, 36f), Visible = false };
        _hatBuy.AddThemeFontSizeOverride("font_size", 16);
        _hatBuy.Pressed += BuyHat;
        row.AddChild(prev);
        row.AddChild(_hatLabel);
        row.AddChild(next);
        row.AddChild(_hatBuy);
        box.AddChild(row);
    }

    private Button HatArrow(string text, int step)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(44f, 36f) };
        b.AddThemeFontSizeOverride("font_size", 20);
        b.Pressed += () =>
        {
            int n = Hats.All.Count;
            _hatIndex = ((_hatIndex + step) % n + n) % n;
            ShowHat();
        };
        return b;
    }

    private HatDef CurrentHat => Hats.All[Mathf.Clamp(_hatIndex, 0, Hats.All.Count - 1)];

    private void ShowHat()
    {
        if (_shownCharacter is not { } c || _hatLabel is null) return;
        if (_hatCharacter != c)
        {
            _hatCharacter = c;
            HatDef chosen = Hats.SelectedFor(c.Id);
            _hatIndex = 0;
            for (int i = 0; i < Hats.All.Count; i++) if (Hats.All[i].Id == chosen.Id) _hatIndex = i;
        }

        HatDef hat = CurrentHat;
        int n = Hats.All.Count;
        bool buyable = !hat.IsOwned && hat.PearlCost > 0 && hat.UnlockedBy is null;
        if (hat.IsOwned)
        {
            _hatLabel.Text = $"hat: {hat.Name}   ({_hatIndex + 1}/{n})";
            _hatLabel.AddThemeColorOverride("font_color", Ink);
        }
        else
        {
            _hatLabel.Text = $"hat: {hat.Name}  ·  locked — {hat.HowToGet}   ({_hatIndex + 1}/{n})";
            _hatLabel.AddThemeColorOverride("font_color", Locked);
        }
        _hatBuy.Visible = buyable;
        if (buyable)
        {
            _hatBuy.Text = $"BUY {hat.PearlCost}";
            _hatBuy.Disabled = Pearls.Balance < hat.PearlCost;
        }
        ApplyMenuHat();
    }

    private void BuyHat()
    {
        if (_shownCharacter is not { } c) return;
        HatDef hat = CurrentHat;
        if (!Hats.TryBuy(hat)) return;
        Hats.Select(c.Id, hat);
        Profile.Save();
        ShowHat();
    }

    /// <summary>Put the previewed hat on the backdrop hero (locked ones too:
    /// the preview is how a player decides what to work towards).</summary>
    private void ApplyMenuHat()
    {
        if (_hero is null || _shownCharacter is null) return;
        string id = CurrentHat.Id;
        HatOn(_hero, id);
    }

    private static void HatOn(Node n, string id)
    {
        if (n is Visuals.CharacterRig rig) { rig.SetHat(id); return; }
        foreach (Node c in n.GetChildren()) HatOn(c, id);
    }

    private void SaveHatChoice(CharacterDef c)
    {
        if (_hatCharacter == c && CurrentHat.IsOwned) Hats.Select(c.Id, CurrentHat);
    }
}
