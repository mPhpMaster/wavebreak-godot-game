using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The character panel's text, shared by the PLAY page (<see cref="MainMenu"/>)
/// and the CO-OP page (<see cref="CoopPage"/>): name, blurb, starting weapon
/// with the stat line, and the passive. Hidden (secret, not met) characters
/// keep their name and details back.
/// </summary>
public readonly record struct CharacterInfo(string Name, string Blurb, string Stats, string Passive)
{
    public static CharacterInfo Of(CharacterDef c)
    {
        UnlockDef? source = Unlocks.ById(c.UnlockedBy);
        bool hidden = !c.IsAvailable && source is { Secret: true };
        return new CharacterInfo(
            hidden ? "???" : Loc.T(c.DisplayName),
            Loc.T(hidden ? "Someone else walks these shores. You have not met them yet." : c.Blurb),
            hidden ? "" : Loc.F(Combat.Weapons.Signatures.Is(c.StartingWeapon) ? "Signature weapon: {0}" : "Starts with {0}",
                                c.StartingWeaponDef.DisplayName) + "\n" + Characters.Describe(c),
            hidden || c.PassiveName.Length == 0 ? "" : Loc.F("Passive — {0}: {1}", c.PassiveName, c.PassiveText));
    }

    /// <summary>Write it into four labels (any may be null).</summary>
    public void Into(Label? name, Label? blurb, Label? stats, Label? passive)
    {
        if (name is not null) name.Text = Name;
        if (blurb is not null) blurb.Text = Blurb;
        if (stats is not null) stats.Text = Stats;
        if (passive is not null) { passive.Text = Passive; passive.Visible = Passive.Length > 0; }
    }
}
