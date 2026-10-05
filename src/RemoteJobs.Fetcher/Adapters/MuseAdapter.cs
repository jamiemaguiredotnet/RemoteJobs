using System.Text.Json;
using RemoteJobs.Fetcher.Filtering;
using RemoteJobs.Fetcher.Models;

namespace RemoteJobs.Fetcher.Adapters;

/// <summary>
/// The Muse: https://www.themuse.com/developers/api/v2 - free, public, no API key required
/// at all (500 req/hour unauthenticated, which this adapter stays well within). A general
/// job board, but the "Flexible / Remote" location value is a genuine structured remote
/// signal from the source itself, not a text guess.
/// </summary>
public class MuseAdapter : IJobSourceAdapter
{
    public string SourceName => "The Muse";

    // Self-imposed cap (there's no API key limiting this) to keep each run's request count
    // reasonable - 10 pages x 20 results is plenty given how few end up .NET-relevant anyway.
    private const int MaxPages = 10;

    public async Task<List<JobPosting>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var results = new List<JobPosting>();
        var page = 1;
        var pageCount = 1;

        do
        {
            var url = "https://www.themuse.com/api/public/jobs"
                + $"?page={page}"
                + "&category=" + Uri.EscapeDataString("Software Engineering")
                + "&location=" + Uri.EscapeDataString("Flexible / Remote");

            using var resp = await http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));

            pageCount = doc.RootElement.TryGetProperty("page_count", out var pc) && pc.ValueKind == JsonValueKind.Number
                ? pc.GetInt32()
                : 1;

            if (doc.RootElement.TryGetProperty("results", out var jobs))
            {
                foreach (var item in jobs.EnumerateArray())
                {
                    var title = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var company = item.TryGetProperty("company", out var co) && co.TryGetProperty("name", out var cn)
                        ? cn.GetString() ?? "" : "";
                    var description = item.TryGetProperty("contents", out var d) ? d.GetString() ?? "" : "";
                    var applyUrl = item.TryGetProperty("refs", out var refs) && refs.TryGetProperty("landing_page", out var lp)
                        ? lp.GetString() ?? "" : "";

                    var locationNames = item.TryGetProperty("locations", out var locs) && locs.ValueKind == JsonValueKind.Array
                        ? locs.EnumerateArray().Select(l => l.TryGetProperty("name", out var ln) ? ln.GetString() ?? "" : "").Where(x => x != "").ToList()
                        : [];
                    var rawLocation = locationNames.Count == 0 ? "Flexible / Remote" : string.Join(", ", locationNames);

                    // "Flexible / Remote" is Muse's own remote marker, not a real place - feeding the
                    // literal phrase through CountryParser would slash-split it into a bogus "Flexible"
                    // country tag, so treat it as an explicit Worldwide signal and only parse whatever
                    // specific office locations (if any) are listed alongside it.
                    var specificLocations = locationNames
                        .Where(n => !string.Equals(n, "Flexible / Remote", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    var countries = specificLocations.Count == 0
                        ? new List<string> { "Worldwide" }
                        : CountryParser.ParseFreeText(string.Join(", ", specificLocations));

                    var categories = item.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array
                        ? cats.EnumerateArray().Select(c => c.TryGetProperty("name", out var cnn) ? cnn.GetString() ?? "" : "").Where(x => x != "").ToList()
                        : [];
                    var levels = item.TryGetProperty("levels", out var lvls) && lvls.ValueKind == JsonValueKind.Array
                        ? lvls.EnumerateArray().Select(l => l.TryGetProperty("name", out var lnn) ? lnn.GetString() ?? "" : "").Where(x => x != "").ToList()
                        : [];
                    var tags = categories.Concat(levels).ToList();

                    var postedAt = item.TryGetProperty("publication_date", out var pd) && DateTimeOffset.TryParse(pd.GetString(), out var parsed)
                        ? parsed
                        : DateTimeOffset.UtcNow;

                    results.Add(new JobPosting
                    {
                        Title = title,
                        Company = company,
                        ApplyUrl = applyUrl,
                        Source = SourceName,
                        PostedAt = postedAt,
                        Tags = tags,
                        RawLocation = rawLocation,
                        Countries = countries,
                        CompType = CompType.Unknown,
                        RawSalaryText = null,
                        Description = description,
                        RemoteTrust = RemoteTrust.AssumedRemoteUnlessFlagged
                    });
                }
            }

            page++;
        } while (page <= Math.Min(pageCount, MaxPages));

        return results;
    }
}
