using System.Text.RegularExpressions;

namespace RemoteJobs.Fetcher.Filtering;

/// <summary>
/// Heuristic location -> country parsing. Deliberately simple: the raw location
/// string is always kept alongside the parsed result so edge cases can be eyeballed.
/// </summary>
public static class CountryParser
{
    private static readonly Dictionary<string, string> Normalize = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UK"] = "United Kingdom",
        ["U.K."] = "United Kingdom",
        ["United Kingdom"] = "United Kingdom",
        ["US"] = "United States",
        ["U.S."] = "United States",
        ["USA"] = "United States",
        ["U.S.A."] = "United States",
        ["United States of America"] = "United States",
        ["United States"] = "United States",
        ["UAE"] = "United Arab Emirates",
    };

    private static readonly HashSet<string> WorldwideTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "worldwide", "anywhere", "anywhere in the world", "global", "remote", ""
    };

    public static List<string> ParseFreeText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || WorldwideTerms.Contains(raw.Trim()))
            return new List<string> { "Worldwide" };

        var parts = raw.Split(new[] { ',', '/', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<string>();
        foreach (var part in parts)
        {
            // Check the whole part against Worldwide terms first - a part that's just "Remote" on its
            // own would otherwise be entirely consumed by the prefix-strip below (leaving "") and get
            // silently dropped instead of correctly becoming "Worldwide".
            if (WorldwideTerms.Contains(part)) { result.Add("Worldwide"); continue; }

            var cleaned = Regex.Replace(part, @"^(remote[\s-]*(in|from)?|only|the)\s*", "", RegexOptions.IgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(cleaned)) continue;
            if (WorldwideTerms.Contains(cleaned)) { result.Add("Worldwide"); continue; }
            result.Add(Normalize.TryGetValue(cleaned, out var norm) ? norm : cleaned);
        }

        if (result.Count == 0) result.Add("Worldwide");
        return result.Distinct().ToList();
    }

    /// <summary>For sources that already provide a structured list of countries (e.g. Himalayas).</summary>
    public static List<string> NormalizeList(IEnumerable<string>? list)
    {
        var items = list?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
        if (items.Count == 0) return new List<string> { "Worldwide" };
        return items.Select(i => Normalize.TryGetValue(i.Trim(), out var norm) ? norm : i.Trim()).Distinct().ToList();
    }
}
