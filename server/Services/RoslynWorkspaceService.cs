using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace mcp_server.Services;

public class RoslynWorkspaceService : IRoslynWorkspaceService, IDisposable
{
    private readonly ILogger<RoslynWorkspaceService> _logger;
    private readonly ICacheService _cacheService;
    private readonly MSBuildWorkspace _workspace;
    private readonly ConcurrentDictionary<string, Project> _projectCache = new();
    private readonly ConcurrentDictionary<string, Document> _documentCache = new();
    private readonly SemaphoreSlim _workspaceLock = new(1, 1);
    private bool _isDisposed = false;
    private string? _currentSolutionPath = null;
    private const string SOLUTION_PATH_CACHE_KEY = "workspace_solution_path";

    public RoslynWorkspaceService(ILogger<RoslynWorkspaceService> logger, ICacheService cacheService)
    {
        _logger = logger;
        _cacheService = cacheService;
        _workspace = MSBuildWorkspace.Create();
        _workspace.WorkspaceFailed += OnWorkspaceFailed;
    }

    public async Task<bool> LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken = default)
    {
        try
        {
            await _workspaceLock.WaitAsync(cancellationToken);
            
            if (!File.Exists(solutionPath))
            {
                _logger.LogError("Solution file not found: {SolutionPath}", solutionPath);
                return false;
            }

            var normalizedPath = Path.GetFullPath(solutionPath);
            
            // Check if the same solution is already loaded
            if (_currentSolutionPath != null && string.Equals(_currentSolutionPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Solution already loaded: {SolutionPath}", normalizedPath);
                return true;
            }

            _logger.LogInformation("Loading solution: {SolutionPath}", normalizedPath);
            
            var solution = await _workspace.OpenSolutionAsync(normalizedPath, cancellationToken: cancellationToken);
            
            // Clear old caches
            _projectCache.Clear();
            _documentCache.Clear();
            
            // Cache all projects
            foreach (var project in solution.Projects)
            {
                _projectCache.TryAdd(project.Name, project);
                _logger.LogDebug("Cached project: {ProjectName}", project.Name);
            }

            // Update current solution path and persist it
            _currentSolutionPath = normalizedPath;
            await _cacheService.SetAsync(SOLUTION_PATH_CACHE_KEY, normalizedPath, TimeSpan.FromDays(30));

            _logger.LogInformation("Successfully loaded solution with {ProjectCount} projects", solution.Projects.Count());
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load solution: {SolutionPath}", solutionPath);
            return false;
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    public async Task<bool> LoadProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        try
        {
            await _workspaceLock.WaitAsync(cancellationToken);
            
            if (!File.Exists(projectPath))
            {
                _logger.LogError("Project file not found: {ProjectPath}", projectPath);
                return false;
            }

            _logger.LogInformation("Loading project: {ProjectPath}", projectPath);
            
            var project = await _workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);
            _projectCache.TryAdd(project.Name, project);

            _logger.LogInformation("Successfully loaded project: {ProjectName}", project.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load project: {ProjectPath}", projectPath);
            return false;
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    public async Task<IEnumerable<Project>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return _workspace.CurrentSolution.Projects;
    }

    public async Task<Project?> GetProjectByNameAsync(string projectName, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        
        if (_projectCache.TryGetValue(projectName, out var cachedProject))
        {
            return cachedProject;
        }

        var project = _workspace.CurrentSolution.Projects.FirstOrDefault(p => p.Name == projectName);
        if (project != null)
        {
            _projectCache.TryAdd(projectName, project);
        }

        return project;
    }

    public async Task<Document?> GetDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        
        if (_documentCache.TryGetValue(filePath, out var cachedDocument))
        {
            return cachedDocument;
        }

        var normalizedPath = Path.GetFullPath(filePath);
        
        foreach (var project in _workspace.CurrentSolution.Projects)
        {
            var document = project.Documents.FirstOrDefault(d => 
                string.Equals(Path.GetFullPath(d.FilePath ?? ""), normalizedPath, StringComparison.OrdinalIgnoreCase));
            
            if (document != null)
            {
                _documentCache.TryAdd(filePath, document);
                return document;
            }
        }

        return null;
    }

    public async Task<SemanticModel?> GetSemanticModelAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var document = await GetDocumentAsync(filePath, cancellationToken);
        if (document == null)
        {
            return null;
        }

        return await document.GetSemanticModelAsync(cancellationToken);
    }

    public async Task<SyntaxTree?> GetSyntaxTreeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var document = await GetDocumentAsync(filePath, cancellationToken);
        if (document == null)
        {
            return null;
        }

        return await document.GetSyntaxTreeAsync(cancellationToken);
    }

    public async Task<IEnumerable<Diagnostic>> GetDiagnosticsAsync(string? projectName = null, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<Diagnostic>();

        var projects = string.IsNullOrEmpty(projectName)
            ? _workspace.CurrentSolution.Projects
            : _workspace.CurrentSolution.Projects.Where(p => p.Name == projectName);

        foreach (var project in projects)
        {
            try
            {
                var compilation = await project.GetCompilationAsync(cancellationToken);
                if (compilation != null)
                {
                    diagnostics.AddRange(compilation.GetDiagnostics(cancellationToken));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get diagnostics for project: {ProjectName}", project.Name);
            }
        }

        return diagnostics;
    }

    public async Task<string?> GetDocumentTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var document = await GetDocumentAsync(filePath, cancellationToken);
        if (document == null)
        {
            return null;
        }

        var text = await document.GetTextAsync(cancellationToken);
        return text.ToString();
    }

    public async Task<bool> IsWorkspaceLoadedAsync()
    {
        await Task.CompletedTask;
        return _workspace.CurrentSolution.Projects.Any();
    }

    public async Task ClearWorkspaceAsync()
    {
        await _workspaceLock.WaitAsync();
        try
        {
            _projectCache.Clear();
            _documentCache.Clear();
            _workspace.CloseSolution();
            _currentSolutionPath = null;
            await _cacheService.RemoveAsync(SOLUTION_PATH_CACHE_KEY);
            _logger.LogInformation("Workspace cleared");
        }
        finally
        {
            _workspaceLock.Release();
        }
    }

    public async Task<string?> GetCurrentSolutionPathAsync()
    {
        await Task.CompletedTask;
        return _currentSolutionPath;
    }

    public async Task<bool> TryRestoreLastSolutionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var cachedSolutionPath = await _cacheService.GetAsync<string>(SOLUTION_PATH_CACHE_KEY);
            if (string.IsNullOrEmpty(cachedSolutionPath))
            {
                _logger.LogDebug("No cached solution path found");
                return false;
            }

            if (!File.Exists(cachedSolutionPath))
            {
                _logger.LogWarning("Cached solution path no longer exists: {SolutionPath}", cachedSolutionPath);
                await _cacheService.RemoveAsync(SOLUTION_PATH_CACHE_KEY);
                return false;
            }

            _logger.LogInformation("Attempting to restore last solution: {SolutionPath}", cachedSolutionPath);
            return await LoadSolutionAsync(cachedSolutionPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore last solution");
            return false;
        }
    }

    private void OnWorkspaceFailed(object? sender, WorkspaceDiagnosticEventArgs e)
    {
        _logger.LogWarning("Workspace diagnostic: {Kind} - {Message}", e.Diagnostic.Kind, e.Diagnostic.Message);
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _workspace?.Dispose();
            _workspaceLock?.Dispose();
            _isDisposed = true;
        }
    }
}
