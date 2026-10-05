using System.Text.Json;
using System.Text.Json.Serialization;
using RemoteJobs.Fetcher.Adapters;
using RemoteJobs.Fetcher.Filtering;
using RemoteJobs.Fetcher.Models;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("RemoteJobsFetcher/1.0");

IJobSourceAdapter[] adapters =
[
    new RemoteOkAdapter(),
    new RemotiveAdapter(),
    new ArbeitnowAdapter(),
    new HimalayasAdapter(),
    new JobicyAdapter(),
    new WorkingNomadsAdapter(),
    new WeWorkRemotelyAdapter(),
    new MuseAdapter(),
    new AdzunaAdapter(),
    new ReedAdapter(),
    new CareerjetAdapter(),
];

var all = new List<JobPosting>();
var rawCounts = new Dictionary<string, int>();

foreach (var adapter in adapters)
{
    try
    {
        var jobs = await adapter.FetchAsync(http, CancellationToken.None);
        Console.WriteLine($"{adapter.SourceName}: fetched {jobs.Count} jobs");
        rawCounts[adapter.SourceName] = jobs.Count;
        all.AddRange(jobs);
    }
    catch (AdapterNotConfiguredException ex)
    {
        Console.WriteLine($"{adapter.SourceName}: skipped - {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{adapter.SourceName}: FAILED - {ex.Message}");
    }
}

// Some staffing-marketplace listings (Lemon.io postings via Remotive/Working Nomads in particular) pad
// every job with a generic "every stack we recruit for" tag list (40+ entries) and/or the same boilerplate
// sentence in the description, regardless of what the specific role actually is. Relevance is therefore
// judged on title + tags only (never description, which is the noisiest source for this), and an
// abnormally long tag list - a strong tag-salad signal - is excluded from the check entirely.
const int TagSaladThreshold = 15;

bool IsDotNetRelevant(JobPosting j)
{
    var relevantTags = j.Tags.Count > TagSaladThreshold ? Enumerable.Empty<string>() : j.Tags;
    var relevanceText = string.Join(" ", j.Title, string.Join(" ", relevantTags));
    return DotNetRelevance.IsMatch(relevanceText);
}

// Work mode (remote/hybrid/onsite/unknown) is now classified rather than used to hard-reject postings,
// so hybrid and onsite .NET roles are kept in jobs.json and filterable in the UI instead of silently dropped.
var dotNetRelevant = all.Where(IsDotNetRelevant).ToList();
foreach (var j in dotNetRelevant)
{
    var remoteCheckText = string.Join(" ", j.Title, string.Join(" ", j.Tags), j.Description, j.RawLocation);
    j.WorkMode = RemoteRejectFilter.Classify(j.RemoteTrust, remoteCheckText);
}

var deduped = dotNetRelevant
    .GroupBy(j => (Title: j.Title.Trim().ToLowerInvariant(), Company: j.Company.Trim().ToLowerInvariant()))
    .Select(g => g.First())
    .OrderByDescending(j => j.PostedAt)
    .ToList();

Console.WriteLine($"Matched .NET jobs: {dotNetRelevant.Count} ({deduped.Count} after dedupe)");
foreach (var mode in Enum.GetValues<WorkMode>())
    Console.WriteLine($"  {mode}: {deduped.Count(j => j.WorkMode == mode)}");

var outputDir = Path.Combine(AppContext.BaseDirectory, "wwwroot");
Directory.CreateDirectory(outputDir);
var outputPath = Path.Combine(outputDir, "jobs.json");

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};

await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(deduped, jsonOptions));
Console.WriteLine($"Wrote {outputPath}");

var stats = new
{
    GeneratedAt = DateTimeOffset.UtcNow,
    Sources = adapters.Select(a => a.SourceName).Where(rawCounts.ContainsKey).Select(source =>
    {
        var sourceDotNetRelevant = dotNetRelevant.Where(j => j.Source == source).ToList();
        return new
        {
            Source = source,
            Raw = rawCounts[source],
            DotNetRelevant = sourceDotNetRelevant.Count,
            Remote = sourceDotNetRelevant.Count(j => j.WorkMode == WorkMode.Remote),
            Hybrid = sourceDotNetRelevant.Count(j => j.WorkMode == WorkMode.Hybrid),
            Onsite = sourceDotNetRelevant.Count(j => j.WorkMode == WorkMode.Onsite),
            Unknown = sourceDotNetRelevant.Count(j => j.WorkMode == WorkMode.Unknown),
        };
    }).ToList(),
    Totals = new
    {
        Raw = rawCounts.Values.Sum(),
        DotNetRelevant = dotNetRelevant.Count,
        AfterDedupe = deduped.Count,
        Remote = deduped.Count(j => j.WorkMode == WorkMode.Remote),
        Hybrid = deduped.Count(j => j.WorkMode == WorkMode.Hybrid),
        Onsite = deduped.Count(j => j.WorkMode == WorkMode.Onsite),
        Unknown = deduped.Count(j => j.WorkMode == WorkMode.Unknown),
    }
};

var statsPath = Path.Combine(outputDir, "stats.json");
await File.WriteAllTextAsync(statsPath, JsonSerializer.Serialize(stats, jsonOptions));
Console.WriteLine($"Wrote {statsPath}");
