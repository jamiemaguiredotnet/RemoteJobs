using RemoteJobs.Fetcher.Models;

namespace RemoteJobs.Fetcher.Adapters;

public interface IJobSourceAdapter
{
    string SourceName { get; }
    Task<List<JobPosting>> FetchAsync(HttpClient http, CancellationToken ct);
}
