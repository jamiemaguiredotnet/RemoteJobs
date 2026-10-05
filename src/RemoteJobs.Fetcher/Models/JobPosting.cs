using System.Text.Json.Serialization;

namespace RemoteJobs.Fetcher.Models;

public enum CompType
{
    Unknown,
    AnnualSalary,
    DayRate,
    HourlyRate
}

public enum RemoteTrust
{
    /// <summary>Default: source is remote-focused by nature; reject only if hybrid/onsite text is found.</summary>
    AssumedRemoteUnlessFlagged,

    /// <summary>Source is remote-only by nature (e.g. RemoteOK, Himalayas); skip the hybrid/onsite text check entirely.</summary>
    TrustedRemoteOnly,

    /// <summary>Source is a general job board (e.g. Adzuna, Reed) covering non-remote roles too, so a posting
    /// must positively mention "remote" as well as not looking hybrid/onsite.</summary>
    RequiresExplicitRemoteMatch
}

public enum WorkMode
{
    /// <summary>No hybrid/onsite text found, and (for general job boards) an explicit remote mention was found.</summary>
    Remote,

    /// <summary>Text mentions hybrid working or a specific in-office day count (e.g. "3 days a week").</summary>
    Hybrid,

    /// <summary>Text mentions onsite/in-office working with no hybrid qualifier.</summary>
    Onsite,

    /// <summary>General job board posting with no remote, hybrid, or onsite signal either way.</summary>
    Unknown
}

public class JobPosting
{
    public required string Title { get; set; }
    public required string Company { get; set; }
    public required string ApplyUrl { get; set; }
    public required string Source { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Countries { get; set; } = new();
    public string RawLocation { get; set; } = "";
    public CompType CompType { get; set; } = CompType.Unknown;
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public string? Currency { get; set; }
    public string? RawSalaryText { get; set; }
    public WorkMode WorkMode { get; set; } = WorkMode.Unknown;

    /// <summary>Used only for filtering; never written to jobs.json.</summary>
    [JsonIgnore]
    public string Description { get; set; } = "";

    [JsonIgnore]
    public RemoteTrust RemoteTrust { get; set; } = RemoteTrust.AssumedRemoteUnlessFlagged;
}
