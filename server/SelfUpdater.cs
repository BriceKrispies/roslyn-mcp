using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace mcp_server;

internal static class SelfUpdater
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(5) })
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders = { { "User-Agent", "dotnet-lsp-mcp-selfupdate" } },
    };

    private static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private static string UpdateRepo =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UpdateRepo")?.Value
        ?? "BriceKrispies/roslyn-mcp";

    // Run the update flow. Used by the `update` subcommand.
    public static async Task<int> RunUpdateCommandAsync()
    {
        CleanStaleBackup();

        Console.Error.WriteLine($"Current version: {CurrentVersion}");
        Console.Error.WriteLine($"Checking {UpdateRepo} for newer release...");

        string latest;
        try
        {
            latest = await FetchLatestTagAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not fetch latest release: {ex.Message}");
            return 1;
        }

        if (!IsNewer(latest, CurrentVersion))
        {
            Console.Error.WriteLine($"Already up to date (latest: {latest}).");
            return 0;
        }

        Console.Error.WriteLine($"New version available: {latest}. Downloading...");

        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not resolve current executable path.");
        var rid = DetectRid();
        var assetName = $"dotnet-lsp-mcp-{rid}{(OperatingSystem.IsWindows() ? ".exe" : "")}";
        var url = $"https://github.com/{UpdateRepo}/releases/download/{latest}/{assetName}";

        try
        {
            await DownloadAndReplaceAsync(url, exePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Update failed: {ex.Message}");
            return 1;
        }

        Console.Error.WriteLine($"Updated to {latest}. Restart the MCP server to use the new version.");
        return 0;
    }

    // Fire-and-forget check on server startup. Logs a nudge if newer.
    public static void CheckForUpdatesInBackground(ILogger logger)
    {
        CleanStaleBackup();

        _ = Task.Run(async () =>
        {
            try
            {
                var latest = await FetchLatestTagAsync();
                if (IsNewer(latest, CurrentVersion))
                {
                    logger.LogInformation(
                        "A newer version of dotnet-lsp-mcp is available: {Latest} (current: {Current}). Run `dotnet-lsp-mcp update` to upgrade.",
                        latest, CurrentVersion);
                }
            }
            catch
            {
                // Offline / rate-limited / etc — silent.
            }
        });
    }

    private static async Task<string> FetchLatestTagAsync()
    {
        var json = await Http.GetStringAsync($"https://api.github.com/repos/{UpdateRepo}/releases/latest");
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString()
            ?? throw new InvalidOperationException("Missing tag_name in release response.");
        return tag;
    }

    private static bool IsNewer(string latestTag, string currentVersion)
    {
        var latest = StripV(latestTag);
        var current = StripV(currentVersion);
        if (Version.TryParse(PadToThree(latest), out var a) && Version.TryParse(PadToThree(current), out var b))
            return a > b;
        // Fallback: ordinal compare.
        return !string.Equals(latest, current, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripV(string s) => s.StartsWith('v') || s.StartsWith('V') ? s[1..] : s;

    private static string PadToThree(string v)
    {
        var dash = v.IndexOf('-');
        var core = dash >= 0 ? v[..dash] : v;
        var parts = core.Split('.');
        return parts.Length switch
        {
            1 => core + ".0.0",
            2 => core + ".0",
            _ => core,
        };
    }

    private static string DetectRid()
    {
        if (OperatingSystem.IsWindows()) return "win-x64";
        if (OperatingSystem.IsLinux()) return "linux-x64";
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        throw new PlatformNotSupportedException("Unsupported platform for self-update.");
    }

    private static async Task DownloadAndReplaceAsync(string url, string exePath)
    {
        var newPath = exePath + ".new";
        var oldPath = exePath + ".old";

        using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(newPath);
            await resp.Content.CopyToAsync(fs);
        }

        if (!OperatingSystem.IsWindows())
        {
            // Mark executable.
            File.SetUnixFileMode(newPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        // Move running exe aside, then move new into place. Works on both OSes:
        // Unix allows unlink/rename of a running binary; Windows allows rename.
        if (File.Exists(oldPath)) File.Delete(oldPath);
        File.Move(exePath, oldPath);
        File.Move(newPath, exePath);

        if (!OperatingSystem.IsWindows())
        {
            // On Unix we can delete the old file immediately.
            try { File.Delete(oldPath); } catch { /* leave for next startup cleanup */ }
        }
    }

    // Windows can't delete the running exe during update, so we leave it as
    // <exe>.old. Next startup (when it's no longer the running process), delete.
    private static void CleanStaleBackup()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath is null) return;
            var oldPath = exePath + ".old";
            if (File.Exists(oldPath)) File.Delete(oldPath);
        }
        catch
        {
            // Not critical.
        }
    }
}
