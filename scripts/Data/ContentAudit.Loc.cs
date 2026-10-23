using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.Data;

/// <summary>
/// Localisation checks (Meta/Loc, Meta/ArabicText.*):
///   * every UI key in the Arabic tables has a non-empty translation, and the
///     translation keeps the same {n} holes (a lost hole drops a number on
///     screen; an extra one throws);
///   * no key is translated two different ways in two tables;
///   * every content name and description resolves to Arabic, exactly or
///     through a pattern whose holes resolve too;
///   * the Arabic font is in the project and covers Arabic letters;
///   * the text server shapes and lays out bidi text (Arabic needs both).
/// </summary>
public static partial class ContentAudit
{
    private static readonly Regex LocHole = new(@"\{(\d+)(?:[:,][^}]*)?\}");

    private static void AuditLocalisation()
    {
        int uiKeys = 0, contentKeys = 0;
        foreach (var (table, content, pairs) in Loc.Tables())
            foreach (var (en, ar) in pairs)
            {
                if (content) contentKeys++; else uiKeys++;
                Check(!string.IsNullOrWhiteSpace(ar), $"loc {table}: no Arabic for \"{en}\"");
                if (string.IsNullOrWhiteSpace(ar)) continue;
                Check(Holes(en).SequenceEqual(Holes(ar)), $"loc {table}: holes differ for \"{en}\"");
                Check(ar.Any(c => c >= '؀' && c <= 'ۿ') || !en.Any(char.IsLetter),
                      $"loc {table}: Arabic for \"{en}\" has no Arabic letters");
            }
        Check(uiKeys > 0, "loc: no UI table");

        Loc.BuildTable(out List<string> conflicts);
        foreach (string c in conflicts) Check(false, $"loc conflicting translations {c}");

        List<(string Where, string Text)> missing = LocContent.Missing();
        foreach (var (where, text) in missing.Take(40)) Check(false, $"loc content untranslated {where}: \"{text}\"");
        if (missing.Count > 40) Check(false, $"loc content untranslated: {missing.Count - 40} more");

        Font? font = Loc.LoadArabicFont();
        Check(font is not null, $"loc font missing {Loc.FontPath}");
        if (font is not null)
            Check(font.HasChar('ع') && font.HasChar('ب') && font.HasChar('ﻻ'), "loc font lacks Arabic glyphs");
        Check(ThemeDB.FallbackFont.Fallbacks.Count > 0, "loc: Arabic fallback not on the default font");

        bool shaping = Loc.ShapingAvailable(out string server);
        Check(shaping, $"loc text server '{server}' cannot shape/bidi Arabic");

        Line($"loc ui-keys={uiKeys} content-keys={contentKeys} content-strings={LocContent.All().Count()} " +
             $"untranslated={missing.Count} server=\"{server}\" lang={Loc.Language}");
    }

    private static IEnumerable<string> Holes(string s)
        => LocHole.Matches(s).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
}
