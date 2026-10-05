using System.Text.Json;
using RemoteJobs.Fetcher.Filtering;
using RemoteJobs.Fetcher.Models;

namespace RemoteJobs.Fetcher.Adapters;

/// <summary>
/// Jobicy: https://jobicy.com/api/v2/remote-jobs - free, no key, but asks callers
/// not to poll more than once/hour and hard-caps responses at 100 jobs.
/// </summary>
public class JobicyAdapter : IJobSourceAdapter
{
    public string SourceName => "Jobicy";

    public async Task<List<JobPosting>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        using var resp = await http.GetAsync("https://jobicy.com/api/v2/remote-jobs?count=100", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));

        var results = new List<JobPosting>();
        if (!doc.RootElement.TryGetProperty("jobs", out var jobs)) return results;

        foreach (var item in jobs.EnumerateArray())
        {
            var title = item.GetProperty("jobTitle").GetString() ?? "";
            var company = item.TryGetProperty("companyName", out var cn) ? cn.GetString() ?? "" : "";
            var geo = item.TryGetProperty("jobGeo", out var g) ? g.GetString() ?? "" : "";
            var description = item.TryGetProperty("jobExcerpt", out var e) ? e.GetString() ?? "" : "";

            var industries = item.TryGetProperty("jobIndustry", out var ind) && ind.ValueKind == JsonValueKind.Array
                ? ind.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToList()
                : new List<string>();
            var jobTypes = item.TryGetProperty("jobType", out var jt) && jt.ValueKind == JsonValueKind.Array
                ? jt.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToList()
                : new List<string>();
            var tags = industries.Concat(jobTypes).ToList();

            decimal? min = item.TryGetProperty("salaryMin", out var mn) && mn.ValueKind == JsonValueKind.Number ? mn.GetDecimal() : null;
            decimal? max = item.TryGetProperty("salaryMax", out var mx) && mx.ValueKind == JsonValueKind.Number ? mx.GetDecimal() : null;
            var currency = item.TryGetProperty("salaryCurrency", out var cur) ? cur.GetString() : null;
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

            var postedAt = item.TryGetProperty("pubDate", out var pd) && DateTimeOffset.TryParse(pd.GetString(), out var parsed)
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
                RawLocation = string.IsNullOrWhiteSpace(geo) ? "Worldwide" : geo,
                Countries = CountryParser.ParseFreeText(geo),
                CompType = compType,
                MinAmount = min,
                MaxAmount = max,
                Currency = currency,
                RawSalaryText = hasSalary ? $"{min:N0} - {max:N0} {currency} ({period})" : null,
                Description = description,
                RemoteTrust = RemoteTrust.AssumedRemoteUnlessFlagged
            });
        }

        return results;
    }
}
