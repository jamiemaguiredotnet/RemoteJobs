using System.Xml.Linq;
using RemoteJobs.Fetcher.Filtering;
using RemoteJobs.Fetcher.Models;

namespace RemoteJobs.Fetcher.Adapters;

/// <summary>WeWorkRemotely public RSS feed - no key needed. Titles are formatted "Company: Job Title".</summary>
public class WeWorkRemotelyAdapter : IJobSourceAdapter
{
    public string SourceName => "WeWorkRemotely";

    private const string FeedUrl = "https://weworkremotely.com/categories/remote-programming-jobs.rss";

    public async Task<List<JobPosting>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var xml = await http.GetStringAsync(FeedUrl, ct);
        var doc = XDocument.Parse(xml);

        var results = new List<JobPosting>();
        foreach (var item in doc.Descendants("item"))
        {
            var rawTitle = item.Element("title")?.Value ?? "";
            var link = item.Element("link")?.Value ?? "";
            var region = item.Element("region")?.Value ?? "";
            var category = item.Element("category")?.Value ?? "";
            var description = item.Element("description")?.Value ?? "";

            var postedAt = item.Element("pubDate")?.Value is { } pubDateStr && DateTimeOffset.TryParse(pubDateStr, out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow;

            var idx = rawTitle.IndexOf(':');
            string company, title;
            if (idx > 0 && idx < rawTitle.Length - 1)
            {
                company = rawTitle[..idx].Trim();
                title = rawTitle[(idx + 1)..].Trim();
            }
            else
            {
                company = "Unknown";
                title = rawTitle.Trim();
            }

            results.Add(new JobPosting
            {
                Title = title,
                Company = company,
                ApplyUrl = link,
                Source = SourceName,
                PostedAt = postedAt,
                Tags = string.IsNullOrWhiteSpace(category) ? new List<string>() : new List<string> { category },
                RawLocation = string.IsNullOrWhiteSpace(region) ? "Worldwide" : region,
                Countries = CountryParser.ParseFreeText(region),
                CompType = CompType.Unknown,
                RawSalaryText = null,
                Description = description,
                RemoteTrust = RemoteTrust.AssumedRemoteUnlessFlagged
            });
        }

        return results;
    }
}
