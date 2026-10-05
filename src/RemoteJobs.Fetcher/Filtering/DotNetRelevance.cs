using System.Text.RegularExpressions;

namespace RemoteJobs.Fetcher.Filtering;

public static class DotNetRelevance
{
    // Word-boundary matched so ".net" only matches the standalone token (e.g. won't fire on "internet").
    private static readonly Regex Pattern = new(
        @"(?<![a-z0-9])(\.net\b|asp\.net\b|dotnet\b|blazor\b|c#)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsMatch(string combinedText) => Pattern.IsMatch(combinedText);
}
