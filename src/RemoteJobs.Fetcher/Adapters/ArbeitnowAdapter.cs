using System.Text.Json;
using RemoteJobs.Fetcher.Filtering;
using RemoteJobs.Fetcher.Models;

namespace RemoteJobs.Fetcher.Adapters;

/// <summary>Arbeitnow: https://arbeitnow.com/api/job-board-api - free, no key, no salary field.</summary>
public class ArbeitnowAdapter : IJobSourceAdapter
{
    public string SourceName => "Arbeitnow";

    // Each page returns 250 postings (mostly non-remote, and many not in English), so a single
    // page badly undersells how many remote roles are actually on the board. Self-imposed cap
    // (the API's own terms just ask not to abuse it, no numeric limit) - 5 pages x 250 is plenty.
    private const int MaxPages = 5;

    public async Task<List<JobPosting>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var results = new List<JobPosting>();
        var page = 1;

        while (page <= MaxPages)
        {
            using var resp = await http.GetAsync($"https://arbeitnow.com/api/job-board-api?page={page}", ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));

            if (!doc.RootElement.TryGetProperty("data", out var data)) break;

            foreach (var item in data.EnumerateArray())
            {
                // Arbeitnow tells us directly whether a role is remote - trust that over any text heuristic.
                var isRemote = item.TryGetProperty("remote", out var r) && r.ValueKind == JsonValueKind.True;
                if (!isRemote) continue;

                var title = item.GetProperty("title").GetString() ?? "";
                var company = item.TryGetProperty("company_name", out var cn) ? cn.GetString() ?? "" : "";
                var location = item.TryGetProperty("location", out var loc) ? loc.GetString() ?? "" : "";
                var description = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";

                var tags = item.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
                    ? t.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToList()
                    : new List<string>();

                var postedAt = item.TryGetProperty("created_at", out var ca) && ca.ValueKind == JsonValueKind.Number
                    ? DateTimeOffset.FromUnixTimeSeconds(ca.GetInt64())
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

            var hasNext = doc.RootElement.TryGetProperty("links", out var links)
                && links.TryGetProperty("next", out var next)
                && next.ValueKind == JsonValueKind.String;
            if (!hasNext) break;

            page++;
        }

        return results;
    }
}
