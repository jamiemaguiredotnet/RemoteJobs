namespace RemoteJobs.Fetcher.Adapters;

/// <summary>Thrown by adapters that need an API key that hasn't been set, so Program.cs can skip them quietly.</summary>
public class AdapterNotConfiguredException(string message) : Exception(message);
