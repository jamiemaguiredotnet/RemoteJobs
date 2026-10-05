using System.Text.Json;
using RemoteJobs.Fetcher.Filtering;
using RemoteJobs.Fetcher.Models;

namespace RemoteJobs.Fetcher.Adapters;

/// <summary>RemoteOK: https://remoteok.com/api - free, no key, but requires a real User-Agent.</summary>
public class RemoteOkAdapter : IJobSourceAdapter
{
    public string SourceName => "RemoteOK";

    public async Task<List<JobPosting>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://remoteok.com/api");
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; RemoteJobsFetcher/1.0)");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));

        var results = new List<JobPosting>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            // The first array element is a legal-notice object with no "position" field - skip it.
            if (!item.TryGetProperty("position", out var posEl)) continue;

            var title = posEl.GetString() ?? "";
            var company = item.TryGetProperty("company", out var c) ? c.GetString() ?? "" : "";
            var applyUrl = item.TryGetProperty("apply_url", out var a) ? a.GetString() : null;
            applyUrl ??= item.TryGetProperty("url", out var u) ? u.GetString() : "";
            var location = item.TryGetProperty("location", out var l) ? l.GetString() ?? "" : "";
            var description = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";

            var tags = item.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToList()
                : new List<string>();

            var postedAt = item.TryGetProperty("date", out var dt) && DateTimeOffset.TryParse(dt.GetString(), out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow;

            decimal? min = item.TryGetProperty("salary_min", out var sMin) && sMin.ValueKind == JsonValueKind.Number && sMin.GetDecimal() > 0
                ? sMin.GetDecimal() : null;
            decimal? max = item.TryGetProperty("salary_max", out var sMax) && sMax.ValueKind == JsonValueKind.Number && sMax.GetDecimal() > 0
                ? sMax.GetDecimal() : null;

            var combined = string.Join(" ", title, string.Join(" ", tags), description);
            var hasSalary = min.HasValue || max.HasValue;
            var compType = hasSalary ? SalaryParser.DetermineStructuredType(combined) : CompType.Unknown;

            results.Add(new JobPosting
            {
                Title = title,
                Company = company,
                ApplyUrl = applyUrl ?? "",
                Source = SourceName,
                PostedAt = postedAt,
                Tags = tags,
                RawLocation = string.IsNullOrWhiteSpace(location) ? "Worldwide" : location,
                Countries = CountryParser.ParseFreeText(location),
                CompType = compType,
                MinAmount = min,
                MaxAmount = max,
                Currency = hasSalary ? "USD" : null,
                RawSalaryText = hasSalary ? $"{min:N0} - {max:N0} USD (RemoteOK)" : null,
                Description = description,
                RemoteTrust = RemoteTrust.TrustedRemoteOnly
            });
        }

        return results;
    }
}
