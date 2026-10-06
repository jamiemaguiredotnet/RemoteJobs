using System.Text.RegularExpressions;

namespace RemoteJobs.Fetcher.Filtering;

public static class DotNetRelevance
{
    // Word-boundary matched so ".net" only matches the standalone token (won't fire on "internet" -
    // though \.net\b alone already guarantees that, no literal dot there). The leading negative
    // lookbehind is what actually matters for "ai": none of these alternatives has its own leading
    // \b, so "ai\b" alone would match the trailing "ai" in any word ending in those two letters with
    // a word boundary right after - Dubai, Mumbai, Chennai, Shanghai, all genuinely false-positive
    // without it. AI/ML terms are matched as an equally first-class category alongside .NET, not a
    // narrower .NET+AI intersection - a pure AI/ML role with no .NET in sight is intentionally in scope.
    private static readonly Regex Pattern = new(
        @"(?<![a-z0-9])(\.net\b|asp\.net\b|dotnet\b|blazor\b|c#|ai\b|llm\b|genai\b|nlp\b|machine learning|generative ai|artificial intelligence|agentic ai|ml engineer)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsMatch(string combinedText) => Pattern.IsMatch(combinedText);
}
