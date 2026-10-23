using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Godot;

namespace Wavebreak.Meta;

/// <summary>
/// Language: English (the source text, and the default) or Arabic.
///
/// Every string in the game is written in English, and the English text *is*
/// the translation key. The Arabic side lives in <see cref="ArabicText"/>: one
/// partial-class file per area (menus, HUD, level-up, content...), each a plain
/// array of (English, Arabic) pairs. That table is loaded into a Godot
/// <see cref="Translation"/> for locale "ar" and registered with the
/// <see cref="TranslationServer"/>, so:
///
///   * any Label/Button whose text is a whole key is translated by the engine's
///     own auto-translate, and re-translated live when the locale changes;
///   * code that builds text calls <see cref="T"/> (a lookup) or <see cref="F"/>
///     (a lookup, then string.Format with {0}-style holes) at the point where it
///     sets the text.
///
/// Arabic also flips the root window's layout direction to right-to-left, so
/// containers mirror and labels align to their start edge, and adds Noto Sans
/// Arabic UI (OFL, assets/fonts; the UI cut has the same line metrics as the Latin default, so Arabic rows are no taller) as a fallback on the engine's default font so
/// mixed Arabic/Latin text shapes in one line. Shaping and bidi come from the
/// default Advanced text server (ICU + HarfBuzz); the content audit checks it.
///
/// Stored in <c>user://settings.cfg</c> under <c>[general] language</c>,
/// load-then-write like the other settings. Automated launches never read or
/// write it: they run in English unless <c>--lang=ar</c> asks otherwise (which
/// is how menu captures prove the Arabic screens).
/// </summary>
public static class Loc
{
    public const string English = "en";
    public const string Arabic = "ar";

    /// <summary>Codes and native names, in the order the settings page cycles them.</summary>
    public static readonly string[] Codes = { English, Arabic };
    public static readonly string[] NativeNames = { "English", "العربية" };

    public const string FontPath = "res://assets/fonts/NotoSansArabicUI-SemiBold.ttf";

    private const string Path = "user://settings.cfg";
    private const string Section = "general";

    private static bool _loaded;
    private static bool _fontReady;
    private static bool _registered;
    private static Dictionary<string, string>? _ar;

    public static string Language { get; private set; } = English;
    public static bool IsArabic => Language == Arabic;
    public static bool IsRtl => IsArabic;

    /// <summary>Raised after the language changes (already applied to the tree).</summary>
    public static event Action? Changed;

    /// <summary>The whole Arabic table, English key to Arabic text.</summary>
    public static IReadOnlyDictionary<string, string> ArabicTable => _ar ??= BuildTable(out _);

    // ================================================================ lookup

    /// <summary>The text in the current language: the key itself in English, or
    /// when no translation exists. Besides exact keys, any table key holding
    /// {0}-style holes is also a *pattern*: "Seasoned: Rimeheart" matches
    /// "Seasoned: {0}", and the hole is translated in turn (numbers pass
    /// through). So code that builds English with interpolation can simply wrap
    /// the result in T(), as long as the template is in the table.</summary>
    public static string T(string english)
    {
        if (!IsArabic || string.IsNullOrEmpty(english)) return english;
        if (ArabicTable.TryGetValue(english, out string? ar) && ar.Length > 0) return ar;
        if (Cache.TryGetValue(english, out string? hit)) return hit;
        string result = MatchPattern(english, strict: false) ?? english;
        if (Cache.Count > 20000) Cache.Clear();
        Cache[english] = result;
        return result;
    }

    /// <summary>Translate a format string, then fill it: <c>F("Level {0}", n)</c>.
    /// String arguments are translated too (names, stat words), numbers are not.</summary>
    public static string F(string englishFormat, params object?[] args)
    {
        if (!IsArabic) return string.Format(englishFormat, args);
        var local = new object?[args.Length];
        for (int i = 0; i < args.Length; i++) local[i] = args[i] is string s ? T(s) : args[i];
        try { return string.Format(T(englishFormat), local); }
        catch (FormatException) { return string.Format(englishFormat, args); }
    }

    /// <summary>Arabic text for a key, exact or through a pattern whose every
    /// wordy hole is itself translated; null otherwise (used by the audit).</summary>
    public static string? ArabicFor(string english)
    {
        if (ArabicTable.TryGetValue(english, out string? ar) && ar.Length > 0) return ar;
        return MatchPattern(english, strict: true);
    }

    private static readonly Dictionary<string, string> Cache = new(StringComparer.Ordinal);
    private static List<(Regex Re, string Ar, int[] Slots)>? _patterns;
    private static readonly Regex Hole = new(@"\{(\d+)(?:[:,][^}]*)?\}", RegexOptions.CultureInvariant);

    private static List<(Regex Re, string Ar, int[] Slots)> Patterns => _patterns ??= BuildPatterns();

    private static List<(Regex Re, string Ar, int[] Slots)> BuildPatterns()
    {
        var list = new List<(Regex Re, string Ar, int[] Slots, int Literal)>();
        foreach (var (en, ar) in ArabicTable)
        {
            if (en.IndexOf('{') < 0 || ar.Length == 0) continue;
            MatchCollection holes = Hole.Matches(en);
            if (holes.Count == 0) continue;
            var sb = new StringBuilder("^");
            var slots = new List<int>();
            int last = 0, literal = 0;
            foreach (Match h in holes)
            {
                string lit = en[last..h.Index];
                sb.Append(Regex.Escape(lit));
                literal += lit.Trim().Length;
                sb.Append("(.+?)");
                slots.Add(int.Parse(h.Groups[1].Value));
                last = h.Index + h.Length;
            }
            string tail = en[last..];
            sb.Append(Regex.Escape(tail)).Append('$');
            literal += tail.Trim().Length;
            if (literal < 2) continue; // "{0} {1}" would match anything
            list.Add((new Regex(sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant), ar, slots.ToArray(), literal));
        }
        list.Sort((a, b) => b.Literal.CompareTo(a.Literal));
        return list.ConvertAll(p => (p.Re, p.Ar, p.Slots));
    }

    private static string? MatchPattern(string english, bool strict, int depth = 0)
    {
        if (depth > 4) return null;
        foreach (var (re, arFmt, slots) in Patterns)
        {
            Match m = re.Match(english);
            if (!m.Success) continue;
            var args = new object?[slots.Length == 0 ? 0 : slots.Max() + 1];
            bool ok = true;
            for (int i = 0; i < slots.Length; i++)
            {
                string part = m.Groups[i + 1].Value;
                string? local = part;
                if (HasLetter(part))
                {
                    local = ArabicTable.TryGetValue(part, out string? a) && a.Length > 0 ? a : MatchPattern(part, strict, depth + 1);
                    if (local is null)
                    {
                        if (strict) { ok = false; break; }
                        local = part;
                    }
                }
                args[slots[i]] = local;
            }
            if (!ok) continue;
            try { return string.Format(arFmt, args); }
            catch (FormatException) { continue; }
        }
        return null;
    }

    private static bool HasLetter(string s)
    {
        foreach (char c in s) if (char.IsLetter(c)) return true;
        return false;
    }

    // ================================================================ setup

    /// <summary>Load the setting (once) and make the tree match it: translation
    /// registered, Arabic font fallback in place, locale and layout direction set.
    /// Safe to call from every scene's _Ready.</summary>
    public static void Init(Node anyNode)
    {
        Load();
        EnsureFont();
        EnsureTranslation();
        Apply(anyNode.GetTree());
    }

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        Launch.Parse();
        string? lang = Launch.Language;
        if (lang is null && !Launch.Automated)
        {
            var cfg = new ConfigFile();
            if (cfg.Load(Path) == Error.Ok)
            {
                Variant v = cfg.GetValue(Section, "language", English);
                if (v.VariantType == Variant.Type.String) lang = v.AsString();
            }
        }
        Language = Array.IndexOf(Codes, lang) >= 0 ? lang! : English;
    }

    /// <summary>Switch language, apply it to the tree and persist it (never in
    /// an automated run).</summary>
    public static void SetLanguage(string code, SceneTree? tree)
    {
        if (Array.IndexOf(Codes, code) < 0) code = English;
        Load();
        if (code == Language) return;
        Language = code;
        EnsureFont();
        EnsureTranslation();
        if (tree is not null) Apply(tree);
        Save();
        Changed?.Invoke();
    }

    public static string NativeName(string code)
    {
        int i = Array.IndexOf(Codes, code);
        return i >= 0 ? NativeNames[i] : code;
    }

    private static void Save()
    {
        if (Launch.Automated) return;
        var cfg = new ConfigFile();
        cfg.Load(Path); // keep every other section
        cfg.SetValue(Section, "language", Language);
        Error err = cfg.Save(Path);
        if (err != Error.Ok) GD.PushWarning($"[settings] could not save {Path}: {err}");
    }

    private static void Apply(SceneTree tree)
    {
        TranslationServer.SetLocale(Language);
        tree.Root.SetLayoutDirection(IsRtl ? Window.LayoutDirection.Rtl : Window.LayoutDirection.Ltr);
    }

    private static void EnsureTranslation()
    {
        if (_registered) return;
        _registered = true;
        var tr = new Translation { Locale = Arabic };
        foreach (var (en, ar) in ArabicTable) tr.AddMessage(en, ar);
        TranslationServer.AddTranslation(tr);
    }

    /// <summary>Noto Sans Arabic as a fallback on the engine's default font —
    /// every control and every custom draw in the game uses that font, so one
    /// fallback covers them all and Latin keeps its own face. Added in English
    /// too: the language picker shows "العربية" either way.</summary>
    private static void EnsureFont()
    {
        if (_fontReady) return;
        _fontReady = true;
        Font? arabic = LoadArabicFont();
        if (arabic is null)
        {
            GD.PushWarning($"[loc] no Arabic font at {FontPath}");
            return;
        }
        Font baseFont = ThemeDB.FallbackFont;
        Godot.Collections.Array<Font> fallbacks = baseFont.Fallbacks;
        if (!fallbacks.Contains(arabic)) fallbacks.Add(arabic);
        baseFont.Fallbacks = fallbacks;
        if (ThemeDB.GetProjectTheme()?.DefaultFont is { } projectFont && projectFont != baseFont)
        {
            Godot.Collections.Array<Font> pf = projectFont.Fallbacks;
            pf.Add(arabic);
            projectFont.Fallbacks = pf;
        }
    }

    public static Font? LoadArabicFont()
    {
        // Imported (editor or export) first; the raw file as a fallback for a
        // checkout that has not been imported yet.
        if (ResourceLoader.Exists(FontPath) && ResourceLoader.Load(FontPath) is FontFile imported)
            return imported;
        if (!FileAccess.FileExists(FontPath)) return null;
        byte[] data = FileAccess.GetFileAsBytes(FontPath);
        return data.Length == 0 ? null : new FontFile { Data = data };
    }

    /// <summary>True when the active text server shapes and lays out bidi text
    /// (Godot's Advanced server: ICU + HarfBuzz). Arabic needs both.</summary>
    public static bool ShapingAvailable(out string serverName)
    {
        TextServer ts = TextServerManager.GetPrimaryInterface();
        serverName = ts.GetName();
        return ts.HasFeature(TextServer.Feature.Shaping) && ts.HasFeature(TextServer.Feature.BidiLayout);
    }

    // ================================================================ table

    /// <summary>Every (English, Arabic) array declared on <see cref="ArabicText"/>.
    /// Field names starting with "Content" hold content (names/descriptions);
    /// the rest are UI.</summary>
    public static IEnumerable<(string Table, bool Content, (string En, string Ar)[] Pairs)> Tables()
    {
        foreach (FieldInfo f in typeof(ArabicText).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            if (f.GetValue(null) is (string, string)[] pairs)
                yield return (f.Name, f.Name.StartsWith("Content", StringComparison.Ordinal), pairs);
    }

    /// <summary>Merged table. A key that appears twice keeps its first Arabic;
    /// differing duplicates are reported through <paramref name="conflicts"/>.</summary>
    public static Dictionary<string, string> BuildTable(out List<string> conflicts)
    {
        conflicts = new List<string>();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (table, _, pairs) in Tables())
            foreach (var (en, ar) in pairs)
            {
                if (string.IsNullOrEmpty(en)) continue;
                if (map.TryGetValue(en, out string? have))
                {
                    if (have != ar) conflicts.Add($"{table}: \"{en}\"");
                    continue;
                }
                map[en] = ar;
            }
        return map;
    }
}

/// <summary>Arabic translations, split by area across ArabicText.*.cs files.
/// Each file adds one or more static <c>(string En, string Ar)[]</c> fields;
/// <see cref="Loc.Tables"/> finds them by reflection, so a new file needs no
/// registration. Name content tables "Content..." and UI tables anything else.</summary>
public static partial class ArabicText
{
}
