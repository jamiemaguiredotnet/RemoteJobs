using System.Text.RegularExpressions;
using RemoteJobs.Fetcher.Models;

namespace RemoteJobs.Fetcher.Filtering;

/// <summary>
/// Heuristic classifier for a posting's work mode (remote/hybrid/onsite/unknown), based on
/// combined title/tags/description/location text and the source's RemoteTrust tier.
/// </summary>
public static class RemoteRejectFilter
{
    private static readonly Regex HybridPattern = new(
        @"\bhybrid\b|\d+\s*days?\s*(a|/|per)\s*week|\d+\s*days?\s*/\s*week\s*in",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OnsitePattern = new(
        @"\bon[\s-]?site\b|\bin[\s-]office\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExplicitRemotePattern = new(
        @"\bremote\b|\bwork\s*from\s*home\b|\bwfh\b|\bremote[\s-]first\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool LooksHybrid(string combinedText) => HybridPattern.IsMatch(combinedText);

    private static bool LooksOnsite(string combinedText) => OnsitePattern.IsMatch(combinedText);

    /// <summary>For general job boards where remote can't be assumed - requires an affirmative mention.</summary>
    public static bool LooksExplicitlyRemote(string combinedText) => ExplicitRemotePattern.IsMatch(combinedText);

    /// <summary>
    /// Classifies a posting's work mode from its combined text, honoring the source's RemoteTrust tier:
    /// trusted remote-only sources skip the text check entirely, general job boards fall back to
    /// Unknown (rather than Remote) when no signal either way is found.
    /// </summary>
    public static WorkMode Classify(RemoteTrust trust, string combinedText)
    {
        if (trust == RemoteTrust.TrustedRemoteOnly) return WorkMode.Remote;

        if (LooksHybrid(combinedText)) return WorkMode.Hybrid;
        if (LooksOnsite(combinedText)) return WorkMode.Onsite;

        if (trust == RemoteTrust.RequiresExplicitRemoteMatch)
            return LooksExplicitlyRemote(combinedText) ? WorkMode.Remote : WorkMode.Unknown;

        return WorkMode.Remote;
    }
}
