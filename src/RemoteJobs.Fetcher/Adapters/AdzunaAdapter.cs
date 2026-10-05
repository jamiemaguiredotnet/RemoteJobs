using System.Text.Json;
using RemoteJobs.Fetcher.Filtering;
using RemoteJobs.Fetcher.Models;

namespace RemoteJobs.Fetcher.Adapters;

/// <summary>
/// Adzuna: https://developer.adzuna.com/ - requires a free, self-serve API app_id/app_key
/// (sign up yourself; this project never creates accounts on your behalf). Set the
/// ADZUNA_APP_ID and ADZUNA_APP_KEY environment variables - if either is missing this
/// adapter is skipped rather than failing the whole run.
///
/// Adzuna is a general job board covering every job type, not a remote-only one, so it uses
/// the *requires explicit remote match* trust tier. It supports separate per-country indices
/// under the same account/key (`/jobs/{country-code}/search/{page}`) - queried across the UK,
/// US, Canada, Australia, Germany, Netherlands and France. The `what=remote` query param is
/// the English word specifically, so non-English-market results (DE/NL/FR) skew toward postings
/// that happen to use the English term or an English-language listing - a real gap for local-
/// language "Home Office"/"télétravail"-style postings this adapter won't catch.
/// </summary>
public class AdzunaAdapter : IJobSourceAdapter
{
    public string SourceName => "Adzuna";

    // The query is already scoped to remote + .NET/C#/Blazor terms, so more pages means more
    // legitimately-scoped raw postings, not more noise. Kept modest per country now that this
    // queries 7 countries per run against the same account quota - 3 pages x 50 x 7 countries =
    // 21 calls/run. Check your own Adzuna dashboard if you hit a quota error and lower this.
    private const int MaxPagesPerCountry = 3;
    private const int ResultsPerPage = 50;

    private static readonly (string Code, string CountryName, string Currency)[] Countries =
    [
        ("gb", "United Kingdom", "GBP"),
        ("us", "United States", "USD"),
        ("ca", "Canada", "CAD"),
        ("au", "Australia", "AUD"),
        ("de", "Germany", "EUR"),
        ("nl", "Netherlands", "EUR"),
        ("fr", "France", "EUR"),
    ];

    public async Task<List<JobPosting>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var appId = Environment.GetEnvironmentVariable("ADZUNA_APP_ID");
        var appKey = Environment.GetEnvironmentVariable("ADZUNA_APP_KEY");
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appKey))
            throw new AdapterNotConfiguredException("ADZUNA_APP_ID / ADZUNA_APP_KEY not set - see README");

        var results = new List<JobPosting>();

        foreach (var (code, countryName, currency) in Countries)
        {
            for (var page = 1; page <= MaxPagesPerCountry; page++)
            {
                var url = $"https://api.adzuna.com/v1/api/jobs/{code}/search/{page}"
                    + $"?app_id={Uri.EscapeDataString(appId)}"
                    + $"&app_key={Uri.EscapeDataString(appKey)}"
                    + $"&results_per_page={ResultsPerPage}"
                    + "&what=remote"
                    + "&what_or=" + Uri.EscapeDataString(".net c# asp.net blazor dotnet")
                    + "&content-type=application/json";

                using var resp = await http.GetAsync(url, ct);
                resp.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));

                if (!doc.RootElement.TryGetProperty("results", out var jobs) || jobs.GetArrayLength() == 0) break;

                foreach (var item in jobs.EnumerateArray())
                {
                    var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var company = item.TryGetProperty("company", out var co) && co.TryGetProperty("display_name", out var cn)
                        ? cn.GetString() ?? "" : "";
                    var location = item.TryGetProperty("location", out var loc) && loc.TryGetProperty("display_name", out var ld)
                        ? ld.GetString() ?? "" : "";
                    var description = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                    var category = item.TryGetProperty("category", out var cat) && cat.TryGetProperty("label", out var cl) ? cl.GetString() : null;
                    var contractType = item.TryGetProperty("contract_type", out var ctp) ? ctp.GetString() : null;

                    var tags = new List<string>();
                    if (!string.IsNullOrWhiteSpace(category)) tags.Add(category);
                    if (!string.IsNullOrWhiteSpace(contractType)) tags.Add(contractType);

                    decimal? min = item.TryGetProperty("salary_min", out var sMin) && sMin.ValueKind == JsonValueKind.Number ? sMin.GetDecimal() : null;
                    decimal? max = item.TryGetProperty("salary_max", out var sMax) && sMax.ValueKind == JsonValueKind.Number ? sMax.GetDecimal() : null;
                    var isPredicted = item.TryGetProperty("salary_is_predicted", out var sp) && sp.GetString() == "1";
                    var hasSalary = min.HasValue || max.HasValue;

                    var combined = string.Join(" ", title, description, category ?? "");
                    var compType = hasSalary ? SalaryParser.DetermineStructuredType(combined) : CompType.Unknown;

                    var postedAt = item.TryGetProperty("created", out var cr) && DateTimeOffset.TryParse(cr.GetString(), out var parsed)
                        ? parsed
                        : DateTimeOffset.UtcNow;

                    results.Add(new JobPosting
                    {
                        Title = title,
                        Company = company,
                        ApplyUrl = item.TryGetProperty("redirect_url", out var ru) ? ru.GetString() ?? "" : "",
                        Source = SourceName,
                        PostedAt = postedAt,
                        Tags = tags,
                        RawLocation = string.IsNullOrWhiteSpace(location) ? countryName : location,
                        Countries = new List<string> { countryName },
                        CompType = compType,
                        MinAmount = min,
                        MaxAmount = max,
                        Currency = hasSalary ? currency : null,
                        RawSalaryText = hasSalary ? $"{min:N0} - {max:N0} {currency} (Adzuna{(isPredicted ? ", estimated" : "")})" : null,
                        Description = description,
                        RemoteTrust = RemoteTrust.RequiresExplicitRemoteMatch
                    });
                }

                if (jobs.GetArrayLength() < ResultsPerPage) break;
            }
        }

        return results;
    }
}
