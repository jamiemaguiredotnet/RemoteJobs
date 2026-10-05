using System.Text.Json;
using RemoteJobs.Fetcher.Filtering;
using RemoteJobs.Fetcher.Models;

namespace RemoteJobs.Fetcher.Adapters;

/// <summary>
/// Remotive: https://remotive.com/api/remote-jobs - free, no key. Remotive asks callers
/// not to poll more than ~4 times/day; a scheduled task once every few hours is plenty.
/// </summary>
public class RemotiveAdapter : IJobSourceAdapter
{
    public string SourceName => "Remotive";

    public async Task<List<JobPosting>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        using var resp = await http.GetAsync("https://remotive.com/api/remote-jobs", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));

        var results = new List<JobPosting>();
        if (!doc.RootElement.TryGetProperty("jobs", out var jobs)) return results;

        foreach (var item in jobs.EnumerateArray())
        {
            var title = item.GetProperty("title").GetString() ?? "";
            var company = item.TryGetProperty("company_name", out var cn) ? cn.GetString() ?? "" : "";
            var location = item.TryGetProperty("candidate_required_location", out var loc) ? loc.GetString() ?? "" : "";
            var description = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            var salaryText = item.TryGetProperty("salary", out var s) ? s.GetString() : null;

            var tags = item.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToList()
                : new List<string>();

            var postedAt = item.TryGetProperty("publication_date", out var pd) && DateTimeOffset.TryParse(pd.GetString(), out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow;

            var (compType, min, max, currency) = SalaryParser.ParseFreeText(salaryText);

            results.Add(new JobPosting
            {
                Title = title,
                Company = company,
                ApplyUrl = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                Source = SourceName,
                PostedAt = postedAt,
                Tags = tags,
                RawLocation = string.IsNullOrWhiteSpace(location) ? "Worldwide" : location,
                Countries = CountryParser.ParseFreeText(location),
                CompType = compType,
                MinAmount = min,
                MaxAmount = max,
                Currency = currency,
                RawSalaryText = string.IsNullOrWhiteSpace(salaryText) ? null : salaryText,
                Description = description,
                RemoteTrust = RemoteTrust.AssumedRemoteUnlessFlagged
            });
        }

        return results;
    }
}
