using System.Text.RegularExpressions;
using RemoteJobs.Fetcher.Models;

namespace RemoteJobs.Fetcher.Filtering;

public static class SalaryParser
{
    private static readonly Regex DayRatePattern = new(
        @"day[\s-]?rate|per\s*day|/\s*day|\bp\.?d\.?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HourlyPattern = new(
        @"per\s*hour|/\s*hr\b|/\s*hour|hourly", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NumberPattern = new(
        @"(?<currency>[£$€])?\s*(?<num>\d[\d,]*(?:\.\d+)?)\s*(?<scale>[kK])?",
        RegexOptions.Compiled);

    /// <summary>Parses a free-text salary string (e.g. Remotive's "salary" field) into structured values.</summary>
    public static (CompType Type, decimal? Min, decimal? Max, string? Currency) ParseFreeText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (CompType.Unknown, null, null, null);

        var numbers = new List<decimal>();
        string? currency = null;
        var hasScale = false;

        foreach (Match m in NumberPattern.Matches(raw))
        {
            if (!m.Groups["num"].Success) continue;
            var numStr = m.Groups["num"].Value.Replace(",", "");
            if (!decimal.TryParse(numStr, out var num)) continue;

            if (m.Groups["scale"].Success) { num *= 1000; hasScale = true; }
            numbers.Add(num);

            if (currency == null && m.Groups["currency"].Success)
            {
                currency = m.Groups["currency"].Value switch
                {
                    "£" => "GBP",
                    "$" => "USD",
                    "€" => "EUR",
                    _ => null
                };
            }
        }

        if (numbers.Count == 0)
            return (CompType.Unknown, null, null, null);

        currency ??= ExtractCurrencyCode(raw);

        // Without a currency symbol/code or a "k" scale suffix on a number, small bare numbers are
        // more likely to be incidental (e.g. "3 days a week") than a real salary figure.
        if (currency == null && !hasScale && numbers.All(n => n < 1000))
            return (CompType.Unknown, null, null, null);

        var type = DayRatePattern.IsMatch(raw) ? CompType.DayRate
            : HourlyPattern.IsMatch(raw) ? CompType.HourlyRate
            : CompType.AnnualSalary;

        return (type, numbers.Min(), numbers.Max(), currency);
    }

    /// <summary>For sources with structured numeric salary fields: only the comp type needs detecting from text.</summary>
    public static CompType DetermineStructuredType(string combinedText)
    {
        if (DayRatePattern.IsMatch(combinedText)) return CompType.DayRate;
        if (HourlyPattern.IsMatch(combinedText)) return CompType.HourlyRate;
        return CompType.AnnualSalary;
    }

    private static string? ExtractCurrencyCode(string raw)
    {
        foreach (var code in new[] { "USD", "GBP", "EUR", "CAD", "AUD" })
            if (raw.Contains(code, StringComparison.OrdinalIgnoreCase)) return code;
        return null;
    }
}
