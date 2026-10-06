using System.Text.RegularExpressions;
using RemoteJobs.Fetcher.Models;

namespace RemoteJobs.Fetcher.Filtering;

/// <summary>
/// Heuristic classifier for a posting's work mode (remote/hybrid/onsite/unknown), based on
/// combined title/tags/description/location text and the source's RemoteTrust tier.
/// </summary>
public static class RemoteRejectFilter
{
    // Catches both digit and word-form day/frequency counts - "3 days a week", "two days a week",
    // and critically "twice a week" on its own (no "days" at all), which recruiter copy uses a lot
    // ("joining colleagues in the office twice a week") and the digit-only version used to miss entirely.
    private static readonly Regex HybridPattern = new(
        @"\bhybrid\b|\b(?:\d+|once|twice|one|two|three|four|five|six)\s*(?:times?|days?)?\s*(?:a|per|/)\s*week\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "based at their site"/"office-based" catches postings that never say "onsite" literally
    // but plainly are - recruiter copy favors this phrasing over the word "onsite" itself.
    private static readonly Regex OnsitePattern = new(
        @"\bon[\s-]?site\b|\bin[\s-]office\b|\bbased\s+(?:at|in)\s+(?:their|our|the)?\s*(?:site|office)\b|\b(?:office|site)[\s-]based\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // The negative lookahead excludes "remote" when it's describing the product/domain, not the
    // job's work arrangement - "developing remote monitoring systems" is not a remote job.
    private static readonly Regex ExplicitRemotePattern = new(
        @"\bremote\b(?!\s+(?:monitoring|sensing|control|access|support|management|desktop|connectivity|devices?|systems?|session))|\bwork\s*from\s*home\b|\bwfh\b|\bremote[\s-]first\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool LooksHybrid(string combinedText) => HybridPattern.IsMatch(combinedText);

    private static bool LooksOnsite(string combinedText) => OnsitePattern.IsMatch(combinedText);

    /// <summary>For general job boards where remote can't be assumed - requires an affirmative mention.</summary>
    public static bool LooksExplicitlyRemote(string combinedText) => ExplicitRemotePattern.IsMatch(combinedText);

    /// <summary>
    /// Classifies a posting's work mode from its combined text, honoring the source's RemoteTrust tier:
    /// trusted remote-only sources skip the text check entirely, general job boards fall back to
    /// Unknown (rather than Remote) when no signal either way is found, and truncated-description
    /// sources never resolve to Remote at all - a disqualifying detail past the truncation point can't
    /// be ruled out, so the honest answer is Unknown rather than an unverifiable Remote claim.
    /// </summary>
    public static WorkMode Classify(RemoteTrust trust, string combinedText)
    {
        if (trust == RemoteTrust.TrustedRemoteOnly) return WorkMode.Remote;

        if (LooksHybrid(combinedText)) return WorkMode.Hybrid;
        if (LooksOnsite(combinedText)) return WorkMode.Onsite;

        if (trust == RemoteTrust.TruncatedDescription) return WorkMode.Unknown;

        if (trust == RemoteTrust.RequiresExplicitRemoteMatch)
            return LooksExplicitlyRemote(combinedText) ? WorkMode.Remote : WorkMode.Unknown;

        return WorkMode.Remote;
    }
}
