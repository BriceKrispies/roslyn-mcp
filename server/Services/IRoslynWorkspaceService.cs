using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace mcp_server.Services;

public interface IRoslynWorkspaceService
{
    Task<bool> LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken = default);
    Task<bool> LoadProjectAsync(string projectPath, CancellationToken cancellationToken = default);
    Task<IEnumerable<Project>> GetProjectsAsync(CancellationToken cancellationToken = default);
    Task<Project?> GetProjectByNameAsync(string projectName, CancellationToken cancellationToken = default);
    Task<Document?> GetDocumentAsync(string filePath, CancellationToken cancellationToken = default);
    Task<SemanticModel?> GetSemanticModelAsync(string filePath, CancellationToken cancellationToken = default);
    Task<SyntaxTree?> GetSyntaxTreeAsync(string filePath, CancellationToken cancellationToken = default);
    Task<IEnumerable<Diagnostic>> GetDiagnosticsAsync(string? projectName = null, CancellationToken cancellationToken = default);
    Task<string?> GetDocumentTextAsync(string filePath, CancellationToken cancellationToken = default);
    Task<bool> IsWorkspaceLoadedAsync();
    Task ClearWorkspaceAsync();
    Task<string?> GetCurrentSolutionPathAsync();
    Task<bool> TryRestoreLastSolutionAsync(CancellationToken cancellationToken = default);
}
