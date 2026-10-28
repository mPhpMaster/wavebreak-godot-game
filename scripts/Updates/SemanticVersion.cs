using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Wavebreak.Updates;

public sealed record SemanticVersion(int Major, int Minor, int Patch, string PreRelease = "", string Metadata = "")
    : IComparable<SemanticVersion>
{
    private static readonly Regex Pattern = new(@"^[vV]?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public bool IsStable => PreRelease.Length == 0;

    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = new(0, 0, 0);
        if (text is null || text.Length > 160) return false;
        Match match = Pattern.Match(text);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int major)
            || !int.TryParse(match.Groups[2].Value, out int minor) || !int.TryParse(match.Groups[3].Value, out int patch)) return false;
        string pre = match.Groups[4].Value;
        if (pre.Split('.').Any(part => part.Length > 1 && part[0] == '0' && part.All(char.IsAsciiDigit))) return false;
        version = new(major, minor, patch, pre, match.Groups[5].Value); return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        int comparison = Major.CompareTo(other.Major);
        if (comparison == 0) comparison = Minor.CompareTo(other.Minor);
        if (comparison == 0) comparison = Patch.CompareTo(other.Patch);
        if (comparison != 0) return comparison;
        if (IsStable || other.IsStable) return IsStable == other.IsStable ? 0 : IsStable ? 1 : -1;
        string[] left = PreRelease.Split('.'), right = other.PreRelease.Split('.');
        for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            bool a = left[i].All(char.IsAsciiDigit), b = right[i].All(char.IsAsciiDigit);
            comparison = a && b ? left[i].Length.CompareTo(right[i].Length) : a != b ? a ? -1 : 1 : 0;
            if (comparison == 0) comparison = string.CompareOrdinal(left[i], right[i]);
            if (comparison != 0) return comparison;
        }
        return left.Length.CompareTo(right.Length);
    }
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Major}.{Minor}.{Patch}{(PreRelease.Length > 0 ? "-" + PreRelease : "")}{(Metadata.Length > 0 ? "+" + Metadata : "")}");
}
