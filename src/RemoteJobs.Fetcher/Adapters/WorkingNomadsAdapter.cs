using System.Text.Json;
using RemoteJobs.Fetcher.Filtering;
using RemoteJobs.Fetcher.Models;

namespace RemoteJobs.Fetcher.Adapters;

/// <summary>Working Nomads: https://www.workingnomads.com/api/exposed_jobs/ - free, no key, no salary field.</summary>
public class WorkingNomadsAdapter : IJobSourceAdapter
{
    public string SourceName => "Working Nomads";

    public async Task<List<JobPosting>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        using var resp = await http.GetAsync("https://www.workingnomads.com/api/exposed_jobs/", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));

        var results = new List<JobPosting>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var title = item.GetProperty("title").GetString() ?? "";
            var company = item.TryGetProperty("company_name", out var cn) ? cn.GetString() ?? "" : "";
            var location = item.TryGetProperty("location", out var loc) ? loc.GetString() ?? "" : "";
            var description = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            var category = item.TryGetProperty("category_name", out var cat) ? cat.GetString() : null;

            // Working Nomads returns tags as a comma-separated string, not an array.
            var tagsRaw = item.TryGetProperty("tags", out var t) ? t.GetString() ?? "" : "";
            var tags = tagsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (!string.IsNullOrWhiteSpace(category)) tags.Add(category);

            var postedAt = item.TryGetProperty("pub_date", out var pd) && DateTimeOffset.TryParse(pd.GetString(), out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow;

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
                CompType = CompType.Unknown,
                RawSalaryText = null,
                Description = description,
                RemoteTrust = RemoteTrust.AssumedRemoteUnlessFlagged
            });
        }

        return results;
    }
}
