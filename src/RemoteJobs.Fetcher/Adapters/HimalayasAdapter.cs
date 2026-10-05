using System.Text.Json;
using RemoteJobs.Fetcher.Filtering;
using RemoteJobs.Fetcher.Models;

namespace RemoteJobs.Fetcher.Adapters;

/// <summary>Himalayas: https://himalayas.app/jobs/api - free, no key, remote-only board.</summary>
public class HimalayasAdapter : IJobSourceAdapter
{
    public string SourceName => "Himalayas";

    // The API silently caps "limit" at 20 regardless of what's requested, and paginates via an
    // opaque cursor (returned as "nextCursor", re-sent as the "offset" query param) rather than a
    // page number. Self-imposed cap (there's no documented rate limit) so a run doesn't wander
    // too deep into Himalayas' full (non-.NET-specific) remote job listings.
    private const int MaxPages = 15;

    public async Task<List<JobPosting>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var results = new List<JobPosting>();
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = "https://himalayas.app/jobs/api?limit=20"
                + (cursor != null ? "&offset=" + Uri.EscapeDataString(cursor) : "");

            using var resp = await http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));

            if (!doc.RootElement.TryGetProperty("jobs", out var jobs)) break;

            foreach (var item in jobs.EnumerateArray())
            {
                var title = item.GetProperty("title").GetString() ?? "";
                var company = item.TryGetProperty("companyName", out var cn) ? cn.GetString() ?? "" : "";
                var description = item.TryGetProperty("excerpt", out var e) ? e.GetString() ?? "" : "";

                var locations = item.TryGetProperty("locationRestrictions", out var lr) && lr.ValueKind == JsonValueKind.Array
                    ? lr.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToList()
                    : new List<string>();

                var categories = item.TryGetProperty("categories", out var c) && c.ValueKind == JsonValueKind.Array
                    ? c.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToList()
                    : new List<string>();

                decimal? min = item.TryGetProperty("minSalary", out var mn) && mn.ValueKind == JsonValueKind.Number ? mn.GetDecimal() : null;
                decimal? max = item.TryGetProperty("maxSalary", out var mx) && mx.ValueKind == JsonValueKind.Number ? mx.GetDecimal() : null;
                var currency = item.TryGetProperty("currency", out var cur) ? cur.GetString() : null;
                var period = item.TryGetProperty("salaryPeriod", out var sp) ? sp.GetString() : null;
                var hasSalary = min.HasValue || max.HasValue;

                var compType = hasSalary
                    ? period?.ToLowerInvariant() switch
                    {
                        "hour" or "hourly" => CompType.HourlyRate,
                        "day" or "daily" => CompType.DayRate,
                        _ => CompType.AnnualSalary
                    }
                    : CompType.Unknown;

                var postedAt = item.TryGetProperty("pubDate", out var pd) && pd.ValueKind == JsonValueKind.Number
                    ? DateTimeOffset.FromUnixTimeSeconds(pd.GetInt64())
                    : DateTimeOffset.UtcNow;

                var rawLocation = locations.Count == 0 ? "Worldwide" : string.Join(", ", locations);

                results.Add(new JobPosting
                {
                    Title = title,
                    Company = company,
                    ApplyUrl = item.TryGetProperty("applicationLink", out var al) ? al.GetString() ?? "" : "",
                    Source = SourceName,
                    PostedAt = postedAt,
                    Tags = categories,
                    RawLocation = rawLocation,
                    Countries = CountryParser.NormalizeList(locations),
                    CompType = compType,
                    MinAmount = min,
                    MaxAmount = max,
                    Currency = currency,
                    RawSalaryText = hasSalary ? $"{min:N0} - {max:N0} {currency} ({period})" : null,
                    Description = description,
                    RemoteTrust = RemoteTrust.TrustedRemoteOnly
                });
            }

            cursor = doc.RootElement.TryGetProperty("nextCursor", out var nc) ? nc.GetString() : null;
            if (cursor == null || jobs.GetArrayLength() == 0) break;
        }

        return results;
    }
}
