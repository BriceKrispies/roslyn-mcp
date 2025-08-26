using mcp_server.Models;

namespace mcp_server.Services;

public interface ICallGraphService
{
    Task<CallersResult> FindCallersFromLocationAsync(string filePath, int line, int column, int maxDepth = 5, int limit = 100, CancellationToken cancellationToken = default);
    Task<CalleesResult> FindCalleesFromLocationAsync(string filePath, int line, int column, int maxDepth = 5, int limit = 200, CancellationToken cancellationToken = default);
}
