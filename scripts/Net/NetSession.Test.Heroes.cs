using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Combat.Weapons;

namespace Wavebreak.Net;

/// <summary>
/// Co-op check for the heroes trio: with <c>--coop-expect-signature</c> the
/// player must be a hero (Meta/Characters.Heroes.cs), hold their own signature
/// weapon, and have landed signature hits (on the host's real enemies, or on
/// the client's puppets, whose hits are forwarded like every other weapon's).
/// The signatures draw only on the local machine and keep their state per
/// weapon, so nothing else about them crosses the wire.
/// </summary>
public partial class NetSession
{
    private static bool ExpectSignature => OS.GetCmdlineUserArgs().Contains("--coop-expect-signature");

    private string HeroTestCheck(List<string> problems)
    {
        if (!ExpectSignature) return "";
        Meta.CharacterDef c = Meta.Characters.ForThisRun();
        string sig = c.StartingWeapon;
        bool hero = Meta.Characters.IsHero(c.Id);
        bool held = _main?.Weapons is { } w && w.Has(sig);
        if (!hero) problems.Add($"playing {c.Id}, not a hero");
        else if (!held) problems.Add($"{c.Id} does not hold {sig}");
        if (Signatures.Hits == 0) problems.Add($"no {sig} hit landed");
        return $" hero={c.Id} signature={sig} held={(held ? 1 : 0)} sig_hits={Signatures.Hits}";
    }
}
