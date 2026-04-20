using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using ModelContextProtocol.Server;
using Engine.Services;
using mcp_server;
using mcp_server.Tools;
using System;
using System.ComponentModel;

if (args.Length > 0 && args[0] == "update")
{
    return await SelfUpdater.RunUpdateCommandAsync();
}

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Register services
builder.Services.AddSingleton<IMemoryCache, MemoryCache>();
builder.Services.AddSingleton<ICacheService, CacheService>();
builder.Services.AddSingleton<IRoslynWorkspaceService, RoslynWorkspaceService>();
builder.Services.AddSingleton<IProjectAnalysisService, ProjectAnalysisService>();
builder.Services.AddSingleton<ICallGraphService, CallGraphService>();
builder.Services.AddSingleton<IMediatRMappingService, MediatRMappingService>();
builder.Services.AddTransient<RoslynLspTools>();

// Configure MCP server
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

// Kick off a background update check — logs a nudge to stderr if a newer
// version is available on GitHub releases.
SelfUpdater.CheckForUpdatesInBackground(app.Services.GetRequiredService<ILogger<Program>>());

// Initialize cache service
var cacheService = app.Services.GetRequiredService<ICacheService>();
await cacheService.LoadFromDiskAsync();

// Try to restore the last loaded solution (makes workspace "sticky")
var workspaceService = app.Services.GetRequiredService<IRoslynWorkspaceService>();
await workspaceService.TryRestoreLastSolutionAsync();

// Ensure cache is saved on shutdown
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    try
    {
        cacheService.SaveToDiskAsync().GetAwaiter().GetResult();
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Failed to save cache during application shutdown");
    }
});

await app.RunAsync();
return 0;

[McpServerToolType]
public static class EchoTool
{
    [McpServerTool, Description("Echoes the SolutionPath environment variable.")]
    public static string EchoSolutionPath() => Environment.GetEnvironmentVariable("SolutionPath") ?? string.Empty;
}
