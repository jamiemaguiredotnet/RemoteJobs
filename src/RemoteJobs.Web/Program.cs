using System.Runtime.CompilerServices;

// Serves whatever RemoteJobs.Fetcher last wrote to its Debug output wwwroot folder,
// so pressing F5 on this project is a plain static-file server for local development.
// No relation to the fetcher's own output otherwise - re-run the fetcher and refresh
// the browser to see fresh data, no restart of this project needed.
var wwwroot = GetFetcherWwwRoot();
Directory.CreateDirectory(wwwroot);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = wwwroot
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();

static string GetFetcherWwwRoot([CallerFilePath] string sourceFile = "") =>
    Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(sourceFile)!, "..", "RemoteJobs.Fetcher", "bin", "Debug", "net9.0", "wwwroot"));
