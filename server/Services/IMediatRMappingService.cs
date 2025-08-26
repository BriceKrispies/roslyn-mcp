using Microsoft.CodeAnalysis;

namespace mcp_server.Services;

public interface IMediatRMappingService
{
    Task BuildMappingsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<HandlerMapping>> GetHandlerMappingsAsync(CancellationToken cancellationToken = default);
    Task<HandlerMapping?> FindHandlerForRequestAsync(string requestTypeName, CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> FindRequestsForHandlerAsync(string handlerTypeName, CancellationToken cancellationToken = default);
}

public class HandlerMapping
{
    public string RequestType { get; set; } = string.Empty;
    public string RequestFullName { get; set; } = string.Empty;
    public string HandlerType { get; set; } = string.Empty;
    public string HandlerFullName { get; set; } = string.Empty;
    public string ResponseType { get; set; } = string.Empty;
    public string HandlerMethodName { get; set; } = "Handle";
    public string HandlerFilePath { get; set; } = string.Empty;
    public int HandlerLine { get; set; }
    public int HandlerColumn { get; set; }
    public bool IsCommand { get; set; } // true for IRequest, false for IRequest<T>
}
