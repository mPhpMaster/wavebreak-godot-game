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

    /// <summary><c>--coop-expect-look=&lt;lookId&gt;</c>: this player must wear
    /// that body, and <c>--coop-expect-remote-look=&lt;lookId&gt;</c>: some remote
    /// player must (so a redesigned hero is proved on both sides of the wire,
    /// even when its signature is too short-ranged for the co-op bots to land).</summary>
    private static string? ArgValue(string flag)
    {
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a.StartsWith(flag + "=")) return a[(flag.Length + 1)..];
        return null;
    }

    private string HeroTestCheck(List<string> problems)
    {
        string? wantLocal = ArgValue("--coop-expect-look"), wantRemote = ArgValue("--coop-expect-remote-look");
        if (!ExpectSignature && wantLocal is null && wantRemote is null) return "";
        if (!ExpectSignature)
        {
            string mine = RigLook(_main?.Player);
            var others = _remotes.Values.Where(IsInstanceValid).Select(r => RigLook(r)).ToList();
            if (wantLocal is not null && mine != "hero:" + wantLocal) problems.Add($"local look {mine}, expected hero:{wantLocal}");
            if (wantRemote is not null && !others.Contains("hero:" + wantRemote)) problems.Add($"no remote wears hero:{wantRemote} ({string.Join(",", others)})");
            return $" look={mine} remote_looks={string.Join(",", others)}";
        }
        Meta.CharacterDef c = Meta.Characters.ForThisRun();
        string sig = c.StartingWeapon;
        bool hero = Meta.Characters.IsHero(c.Id);
        bool held = _main?.Weapons is { } w && w.Has(sig);
        if (!hero) problems.Add($"playing {c.Id}, not a hero");
        else if (!held) problems.Add($"{c.Id} does not hold {sig}");
        if (Signatures.Hits == 0) problems.Add($"no {sig} hit landed");

        // Looks: the local hero and every remote hero wear their own hand-built
        // body (Visuals/HeroModels.cs), not a cube pet or the default.
        string localLook = RigLook(_main?.Player);
        if (hero && localLook != "hero:" + c.LookId) problems.Add($"local {c.Id} wears {localLook}, not hero:{c.LookId}");
        var remoteLooks = new List<string>();
        foreach (RemotePlayer r in _remotes.Values)
        {
            if (!IsInstanceValid(r)) continue;
            string look = RigLook(r);
            remoteLooks.Add(look);
            if (!look.StartsWith("hero:hero-")) problems.Add($"remote {r.DisplayName} wears {look}, not a hero body");
        }
        if (wantLocal is not null && localLook != "hero:" + wantLocal) problems.Add($"local look {localLook}, expected hero:{wantLocal}");
        if (wantRemote is not null && !remoteLooks.Contains("hero:" + wantRemote)) problems.Add($"no remote wears hero:{wantRemote}");
        return $" hero={c.Id} signature={sig} held={(held ? 1 : 0)} sig_hits={Signatures.Hits}" +
               $" look={localLook} remote_looks={string.Join(",", remoteLooks)}";
    }

    /// <summary>The model id of the first rig under <paramref name="n"/>.</summary>
    private static string RigLook(Node? n)
    {
        if (n is null) return "-";
        if (n is Visuals.CharacterRig rig) return rig.Spec.Id;
        foreach (Node child in n.GetChildren())
        {
            string s = RigLook(child);
            if (s != "-") return s;
        }
        return "-";
    }
}
