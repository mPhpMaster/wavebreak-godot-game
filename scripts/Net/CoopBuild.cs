using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Godot;
using Wavebreak.Data;
using Wavebreak.Meta;

namespace Wavebreak.Net;

/// <summary>
/// What two machines must share to play together: the game version and a hash
/// of the content catalogs. <see cref="Coop.Protocol"/> is hand-bumped and easy
/// to forget; these two change by themselves. Enemies travel as catalog indexes,
/// so the hash keeps catalog order on purpose: a reorder is a real mismatch.
/// Both are compared in the handshake (NetSession.Handshake.cs) and published in
/// the Steam lobby data.
/// </summary>
public static class CoopBuild
{
    private static string? _version, _content;

    public static string Version => _version ??= TestOverride("--coop-fake-version=")
        ?? ProjectSettings.GetSetting("application/config/version", "0.0.0").AsString();

    /// <summary>Eight hex digits over the protocol tag and every catalog's ids.</summary>
    public static string Content => _content ??= TestOverride("--coop-fake-content=") ?? HashContent();

    /// <summary>Both facts as one value, for the Steam lobby search.</summary>
    public static string Tag => $"{Version}#{Content}";

    public static bool Matches(string version, string content) => version == Version && content == Content;

    /// <summary>Written on this machine, so it reads in this player's language,
    /// whichever language the host plays in. The hash shows only when the
    /// versions alone would read the same.</summary>
    public static string MismatchText(string hostVersion, string hostContent)
    {
        bool sameVersion = hostVersion == Version;
        return Loc.F("Version mismatch: host {0}, you {1}",
                     sameVersion ? $"{hostVersion} #{hostContent}" : hostVersion,
                     sameVersion ? $"{Version} #{Content}" : Version);
    }

    /// <summary>The automated co-op test poses as another build; a normal launch never does.</summary>
    private static string? TestOverride(string prefix)
        => NetSession.AutoRequested() && Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith(prefix)) is { } arg ? arg[prefix.Length..] : null;

    private static string HashContent()
    {
        var text = new StringBuilder(Coop.Protocol);
        void Add(string kind, IEnumerable<string> ids) => text.Append('\n').Append(kind).Append('=').AppendJoin(',', ids);
        Add("weapons", Catalog.Weapons.Select(w => w.Id));
        Add("items", Catalog.Items.Select(i => i.Id));
        Add("enemies", Catalog.Enemies.Select(e => e.Id));
        Add("tomes", Tomes.All.Select(t => t.Id));
        Add("perks", Perks.All.Select(p => p.Id));
        Add("stages", Stages.All.Select(s => s.Id));
        Add("characters", Characters.All.Select(c => c.Id));
        Add("skins", Characters.All.SelectMany(c => c.Skins).Select(s => s.Id));
        Add("hats", Hats.All.Select(h => h.Id));
        Add("routes", Routes.All.Select(r => r.Id));
        Add("tiers", DifficultyTiers.All.Select(t => t.Id));
        Add("meta_weapons", MetaContent.Weapons.Select(w => w.Id));
        Add("meta_items", MetaContent.Items.Select(i => i.Id));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..8].ToLowerInvariant();
    }
}
