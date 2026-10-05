using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RemoteJobs.Fetcher.Filtering;
using RemoteJobs.Fetcher.Models;

namespace RemoteJobs.Fetcher.Adapters;

/// <summary>
/// Reed: https://www.reed.co.uk/developers/jobseeker - requires a free, self-serve API key
/// (sign up yourself; this project never creates accounts on your behalf). Set the
/// REED_API_KEY environment variable - sent as the HTTP Basic Auth username with an empty
/// password, per Reed's docs. If the key is missing, this adapter is skipped rather than
/// failing the whole run.
///
/// Reed is a UK-only, general job board covering every job type, not a remote-only one.
/// </summary>
public class ReedAdapter : IJobSourceAdapter
{
    public string SourceName => "Reed";

    public async Task<List<JobPosting>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var apiKey = Environment.GetEnvironmentVariable("REED_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AdapterNotConfiguredException("REED_API_KEY not set - see README");

        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://www.reed.co.uk/api/1.0/search?keywords=" + Uri.EscapeDataString(".NET C# Blazor remote") + "&resultsToTake=100");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:")));

        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));

        var results = new List<JobPosting>();
        if (!doc.RootElement.TryGetProperty("results", out var jobs)) return results;

        foreach (var item in jobs.EnumerateArray())
        {
            var title = item.TryGetProperty("jobTitle", out var t) ? t.GetString() ?? "" : "";
            var company = item.TryGetProperty("employerName", out var e) ? e.GetString() ?? "" : "";
            var location = item.TryGetProperty("locationName", out var loc) ? loc.GetString() ?? "" : "";
            var description = item.TryGetProperty("jobDescription", out var d) ? d.GetString() ?? "" : "";
            var contractType = item.TryGetProperty("contractType", out var ct2) ? ct2.GetString() : null;
            var jobType = item.TryGetProperty("jobType", out var jt) ? jt.GetString() : null;

            var tags = new List<string>();
            if (!string.IsNullOrWhiteSpace(contractType)) tags.Add(contractType);
            if (!string.IsNullOrWhiteSpace(jobType)) tags.Add(jobType);

            decimal? min = item.TryGetProperty("minimumSalary", out var mn) && mn.ValueKind == JsonValueKind.Number ? mn.GetDecimal() : null;
            decimal? max = item.TryGetProperty("maximumSalary", out var mx) && mx.ValueKind == JsonValueKind.Number ? mx.GetDecimal() : null;
            var currency = item.TryGetProperty("currency", out var cur) ? cur.GetString() : null;
            currency = string.IsNullOrWhiteSpace(currency) ? "GBP" : currency;
            var hasSalary = min.HasValue || max.HasValue;

            var combined = string.Join(" ", title, description, contractType ?? "", jobType ?? "");
            var isContractRole = string.Equals(contractType, "Contract", StringComparison.OrdinalIgnoreCase);
            var compType = hasSalary
                ? (isContractRole ? SalaryParser.DetermineStructuredType(combined) : CompType.AnnualSalary)
                : CompType.Unknown;

            var postedAt = item.TryGetProperty("date", out var pd) && DateTimeOffset.TryParse(pd.GetString(), out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow;

            results.Add(new JobPosting
            {
                Title = title,
                Company = company,
                ApplyUrl = item.TryGetProperty("jobUrl", out var u) ? u.GetString() ?? "" : "",
                Source = SourceName,
                PostedAt = postedAt,
                Tags = tags,
                RawLocation = string.IsNullOrWhiteSpace(location) ? "United Kingdom" : location,
                Countries = new List<string> { "United Kingdom" },
                CompType = compType,
                MinAmount = min,
                MaxAmount = max,
                Currency = currency,
                RawSalaryText = hasSalary ? $"{min:N0} - {max:N0} {currency} (Reed)" : null,
                Description = description,
                RemoteTrust = RemoteTrust.RequiresExplicitRemoteMatch
            });
        }

        return results;
    }
}
