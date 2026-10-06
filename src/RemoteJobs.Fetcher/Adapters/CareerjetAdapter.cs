using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RemoteJobs.Fetcher.Filtering;
using RemoteJobs.Fetcher.Models;

namespace RemoteJobs.Fetcher.Adapters;

/// <summary>
/// Careerjet: https://www.careerjet.com/partners/api/ - requires a free, self-serve API key
/// (sign up yourself; this project never creates accounts on your behalf). Set the
/// CAREERJET_API_KEY environment variable - sent as the HTTP Basic Auth username with an empty
/// password, per Careerjet's docs. If the key is missing, this adapter is skipped rather than
/// failing the whole run.
///
/// **Careerjet also requires whitelisting the calling server's outbound IP in its publisher
/// dashboard before any request succeeds** (every call otherwise 403s) - there's no API to do
/// that programmatically, so it's on you to keep it current. Set CAREERJET_USER_IP to that same
/// whitelisted IP (sent as the required "user_ip" param); if it's missing, this adapter is
/// skipped too. If you run this from a home connection with a dynamic IP, expect to have to
/// re-whitelist it occasionally - this makes Careerjet the most fragile source here by design,
/// not a bug in this adapter.
///
/// Careerjet is a general job board covering every job type across ~70 country markets, not a
/// remote-only one - uses the same *truncated description* trust tier as Adzuna (see
/// RemoteTrust.TruncatedDescription), as a precaution rather than a confirmed limitation - its
/// docs don't mention a description length cap, but it hasn't been live-tested to rule one out
/// either, and Adzuna's own cap wasn't documented anywhere until it was found by testing. Written
/// against Careerjet's published v4 API docs but not live-tested end-to-end (no test API key or
/// whitelisted IP was available). If you hit an error after setting both env vars, check the
/// console output for the exact HTTP error and open an issue in this repo (or just tell me).
/// </summary>
public class CareerjetAdapter : IJobSourceAdapter
{
    public string SourceName => "Careerjet";

    // Multiple locales per run all draw from the same account, so pages are kept modest here -
    // 2 pages x 100 results x 7 locales = 14 calls, ~1,400 raw postings/run.
    private const int MaxPagesPerLocale = 2;
    private const int PageSize = 100;

    private static readonly (string Locale, string CountryName, string Currency)[] Locales =
    [
        ("en_GB", "United Kingdom", "GBP"),
        ("en_US", "United States", "USD"),
        ("en_CA", "Canada", "CAD"),
        ("en_AU", "Australia", "AUD"),
        ("de_DE", "Germany", "EUR"),
        ("nl_NL", "Netherlands", "EUR"),
        ("fr_FR", "France", "EUR"),
    ];

    public async Task<List<JobPosting>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var apiKey = Environment.GetEnvironmentVariable("CAREERJET_API_KEY");
        var userIp = Environment.GetEnvironmentVariable("CAREERJET_USER_IP");
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(userIp))
            throw new AdapterNotConfiguredException("CAREERJET_API_KEY / CAREERJET_USER_IP not set - see README");

        var results = new List<JobPosting>();

        foreach (var (locale, countryName, defaultCurrency) in Locales)
        {
            for (var page = 1; page <= MaxPagesPerLocale; page++)
            {
                var url = "https://search.api.careerjet.net/v4/query"
                    + "?keywords=" + Uri.EscapeDataString(".NET C# Blazor remote")
                    + "&locale_code=" + Uri.EscapeDataString(locale)
                    + $"&page={page}"
                    + $"&page_size={PageSize}"
                    + "&user_ip=" + Uri.EscapeDataString(userIp)
                    + "&user_agent=" + Uri.EscapeDataString("RemoteJobsFetcher/1.0");

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:")));

                using var resp = await http.SendAsync(req, ct);
                resp.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));

                var type = doc.RootElement.TryGetProperty("type", out var ty) ? ty.GetString() : null;
                if (!string.Equals(type, "JOBS", StringComparison.OrdinalIgnoreCase)) break;
                if (!doc.RootElement.TryGetProperty("jobs", out var jobs) || jobs.GetArrayLength() == 0) break;

                foreach (var item in jobs.EnumerateArray())
                {
                    var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var company = item.TryGetProperty("company", out var co) ? co.GetString() ?? "" : "";
                    var location = item.TryGetProperty("locations", out var loc) ? loc.GetString() ?? "" : "";
                    var description = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";

                    decimal? min = item.TryGetProperty("salary_min", out var sMin) && sMin.ValueKind == JsonValueKind.Number ? sMin.GetDecimal() : null;
                    decimal? max = item.TryGetProperty("salary_max", out var sMax) && sMax.ValueKind == JsonValueKind.Number ? sMax.GetDecimal() : null;
                    var currencyCode = item.TryGetProperty("salary_currency_code", out var cc) ? cc.GetString() : null;
                    currencyCode = string.IsNullOrWhiteSpace(currencyCode) ? defaultCurrency : currencyCode;
                    var salaryType = item.TryGetProperty("salary_type", out var st) ? st.GetString() ?? "" : "";
                    var hasSalary = min.HasValue || max.HasValue;

                    var compType = hasSalary
                        ? salaryType.ToLowerInvariant() switch
                        {
                            var s when s.Contains("hour") => CompType.HourlyRate,
                            var s when s.Contains("day") => CompType.DayRate,
                            _ => CompType.AnnualSalary
                        }
                        : CompType.Unknown;

                    var postedAt = item.TryGetProperty("date", out var pd) && DateTimeOffset.TryParse(pd.GetString(), out var parsed)
                        ? parsed
                        : DateTimeOffset.UtcNow;

                    results.Add(new JobPosting
                    {
                        Title = title,
                        Company = company,
                        ApplyUrl = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                        Source = SourceName,
                        PostedAt = postedAt,
                        Tags = new List<string>(),
                        RawLocation = string.IsNullOrWhiteSpace(location) ? countryName : location,
                        Countries = new List<string> { countryName },
                        CompType = compType,
                        MinAmount = min,
                        MaxAmount = max,
                        Currency = hasSalary ? currencyCode : null,
                        RawSalaryText = hasSalary ? $"{min:N0} - {max:N0} {currencyCode} (Careerjet)" : null,
                        Description = description,
                        RemoteTrust = RemoteTrust.TruncatedDescription
                    });
                }

                if (jobs.GetArrayLength() < PageSize) break;
            }
        }

        return results;
    }
}
