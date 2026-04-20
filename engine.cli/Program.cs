using Engine.Services;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    await Console.Error.WriteAsync(Usage());
    return args.Length == 0 ? 1 : 0;
}

var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton<IMemoryCache, MemoryCache>();
builder.Services.AddSingleton<ICacheService, CacheService>();
builder.Services.AddSingleton<IRoslynWorkspaceService, RoslynWorkspaceService>();
builder.Services.AddSingleton<IProjectAnalysisService, ProjectAnalysisService>();
builder.Services.AddSingleton<ICallGraphService, CallGraphService>();
builder.Services.AddSingleton<IMediatRMappingService, MediatRMappingService>();

var host = builder.Build();
var cache = host.Services.GetRequiredService<ICacheService>();
var workspace = host.Services.GetRequiredService<IRoslynWorkspaceService>();
var analysis = host.Services.GetRequiredService<IProjectAnalysisService>();
var callgraph = host.Services.GetRequiredService<ICallGraphService>();
var mediatr = host.Services.GetRequiredService<IMediatRMappingService>();

await cache.LoadFromDiskAsync();
await workspace.TryRestoreLastSolutionAsync();

int exitCode = 0;
try
{
    var result = await Dispatch(args);
    if (result is not null)
    {
        var json = JsonConvert.SerializeObject(result, Formatting.Indented,
            new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });
        Console.WriteLine(json);
    }
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"ERROR: {ex.GetType().Name}: {ex.Message}");
    exitCode = 1;
}

await cache.SaveToDiskAsync();
return exitCode;

async Task<object?> Dispatch(string[] a) => a[0] switch
{
    "load-solution"    => await workspace.LoadSolutionAsync(a[1]),
    "load-project"     => await workspace.LoadProjectAsync(a[1]),
    "status"           => await Status(),
    "clear"            => await Clear(),
    "mediatr"          => await mediatr.GetHandlerMappingsAsync(),
    "callees"          => await callgraph.FindCalleesFromLocationAsync(
                              a[1], int.Parse(a[2]), int.Parse(a[3]),
                              a.Length > 4 ? int.Parse(a[4]) : 5,
                              a.Length > 5 ? int.Parse(a[5]) : 200),
    "callers"          => await callgraph.FindCallersFromLocationAsync(
                              a[1], int.Parse(a[2]), int.Parse(a[3]),
                              a.Length > 4 ? int.Parse(a[4]) : 5,
                              a.Length > 5 ? int.Parse(a[5]) : 100),
    "impls"            => await analysis.FindImplementationsAsync(a[1], a.Length > 2 ? a[2] : null),
    "refs"             => await analysis.FindReferencesAsync(a[1], a.Length > 2 ? a[2] : null),
    "symbols"          => await analysis.GetSymbolsAsync(a[1],
                              a.Length > 2 && Enum.TryParse<SymbolKind>(a[2], true, out var k) ? k : null),
    "diagnostics"      => await Diagnostics(a.Length > 1 ? a[1] : null),
    "analyze-project"  => await analysis.AnalyzeProjectAsync(a[1]),
    "analyze-solution" => await analysis.AnalyzeSolutionAsync(a[1]),
    "analyze-file"     => await analysis.AnalyzeFileAsync(a[1]),
    _ => throw new ArgumentException($"unknown subcommand '{a[0]}' — try --help"),
};

async Task<object> Status()
{
    var loaded = await workspace.IsWorkspaceLoadedAsync();
    var solutionPath = await workspace.GetCurrentSolutionPathAsync();
    if (!loaded) return new { isLoaded = false, solutionPath };
    var projects = await workspace.GetProjectsAsync();
    return new
    {
        isLoaded = true,
        solutionPath,
        projects = projects.Select(p => new { name = p.Name, path = p.FilePath, language = p.Language }).ToList()
    };
}

async Task<object> Clear()
{
    await workspace.ClearWorkspaceAsync();
    await analysis.InvalidateCacheAsync();
    return new { success = true };
}

async Task<object> Diagnostics(string? projectName)
{
    var ds = await workspace.GetDiagnosticsAsync(projectName);
    return ds.Select(d => new
    {
        id = d.Id,
        severity = d.Severity.ToString(),
        message = d.GetMessage(),
        location = d.Location.IsInSource ? d.Location.GetLineSpan().ToString() : "External",
        filePath = d.Location.SourceTree?.FilePath
    }).ToList();
}

static string Usage() => """
engine — headless Roslyn analysis. JSON to stdout, logs/errors to stderr.

Subcommands:
  load-solution <path>
  load-project  <path>
  status
  clear
  mediatr
  callees    <file> <line> <col> [depth=5] [limit=200]
  callers    <file> <line> <col> [depth=5] [limit=100]
  impls      <interfaceName>     [projectName]
  refs       <symbol>            [projectName]
  symbols    <projectName>       [symbolKind]
  diagnostics                    [projectName]
  analyze-project  <path>
  analyze-solution <path>
  analyze-file     <path>

The last loaded solution is cached on disk, so subsequent invocations
in the same working directory don't need to re-load.
""";
