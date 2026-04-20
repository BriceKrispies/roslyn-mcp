using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Engine.Services;
using Newtonsoft.Json;
using System.ComponentModel;

namespace mcp_server.Tools;

[McpServerToolType]
public class RoslynLspTools
{
    private readonly IProjectAnalysisService _analysisService;
    private readonly IRoslynWorkspaceService _workspaceService;
    private readonly ICallGraphService _callGraphService;
    private readonly IMediatRMappingService _mediatRMappingService;
    private readonly ILogger<RoslynLspTools> _logger;

    public RoslynLspTools(
        IProjectAnalysisService analysisService,
        IRoslynWorkspaceService workspaceService,
        ICallGraphService callGraphService,
        IMediatRMappingService mediatRMappingService,
        ILogger<RoslynLspTools> logger)
    {
        _analysisService = analysisService;
        _workspaceService = workspaceService;
        _callGraphService = callGraphService;
        _mediatRMappingService = mediatRMappingService;
        _logger = logger;
    }

    private async Task<string> ExecuteAsync<T>(string operationName, Func<Task<T>> operation, params object?[] parameters)
    {
        _logger.LogInformation("Executing operation: {OperationName} with parameters: {Parameters}",
            operationName,
            parameters.Length > 0 ? string.Join(", ", parameters.Select(p => p?.ToString() ?? "null")) : "none");

        try
        {
            var result = await operation();
            _logger.LogDebug("Operation {OperationName} completed successfully", operationName);
            return JsonConvert.SerializeObject(result, Formatting.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in operation: {OperationName}", operationName);
            return JsonConvert.SerializeObject(new
            {
                success = false,
                error = ex.Message,
                operationName,
                stackTrace = (string?)null
            }, Formatting.Indented);
        }
    }

    [McpServerTool, Description("Loads a .NET solution and prepares it for analysis. Returns information about loaded projects.")]
    public Task<string> LoadSolution(string solutionPath) =>
        ExecuteAsync<object>(nameof(LoadSolution), async () =>
        {
            var success = await _workspaceService.LoadSolutionAsync(solutionPath);
            if (!success)
            {
                return new { success = false, error = "Failed to load solution" };
            }

            var projects = await _workspaceService.GetProjectsAsync();
            var projectInfo = projects.Select(p => new
            {
                name = p.Name,
                path = p.FilePath,
                language = p.Language,
                documentCount = p.Documents.Count()
            }).ToList();

            return new
            {
                success = true,
                solutionPath,
                projectCount = projectInfo.Count,
                projects = projectInfo
            };
        }, solutionPath);

    [McpServerTool, Description("Loads a single .NET project and prepares it for analysis.")]
    public Task<string> LoadProject(string projectPath) =>
        ExecuteAsync<object>(nameof(LoadProject), async () =>
        {
            var success = await _workspaceService.LoadProjectAsync(projectPath);
            if (!success)
            {
                return new { success = false, error = "Failed to load project" };
            }

            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var project = await _workspaceService.GetProjectByNameAsync(projectName);

            if (project == null)
            {
                return new { success = false, error = "Project not found after loading" };
            }

            return new
            {
                success = true,
                projectName = project.Name,
                projectPath,
                language = project.Language,
                documentCount = project.Documents.Count(),
                documents = project.Documents.Select(d => d.Name).ToList()
            };
        }, projectPath);

    [McpServerTool, Description("Analyzes a project and returns comprehensive analysis results including diagnostics, symbols, and metrics.")]
    public Task<string> AnalyzeProject(string projectPath) =>
        ExecuteAsync(nameof(AnalyzeProject), async () =>
        {
            var result = await _analysisService.AnalyzeProjectAsync(projectPath);

            return new
            {
                projectName = result.ProjectName,
                projectPath = result.ProjectPath,
                totalLines = result.TotalLines,
                sourceFileCount = result.SourceFiles.Count(),
                dependencyCount = result.Dependencies.Count(),
                diagnostics = result.Diagnostics.Select(d => new
                {
                    id = d.Id,
                    severity = d.Severity.ToString(),
                    message = d.GetMessage(),
                    location = d.Location.GetLineSpan().ToString()
                }).ToList(),
                sourceFiles = result.SourceFiles.ToList(),
                dependencies = result.Dependencies.ToList(),
                analyzedAt = result.AnalyzedAt
            };
        }, projectPath);

    [McpServerTool, Description("Analyzes a solution and returns analysis for all projects within it.")]
    public Task<string> AnalyzeSolution(string solutionPath) =>
        ExecuteAsync(nameof(AnalyzeSolution), async () =>
        {
            var result = await _analysisService.AnalyzeSolutionAsync(solutionPath);

            return new
            {
                solutionName = result.SolutionName,
                solutionPath = result.SolutionPath,
                projectCount = result.Projects.Count(),
                totalDiagnostics = result.AllDiagnostics.Count(),
                projects = result.Projects.Select(p => new
                {
                    name = p.ProjectName,
                    path = p.ProjectPath,
                    lines = p.TotalLines,
                    files = p.SourceFiles.Count(),
                    diagnostics = p.Diagnostics.Count(),
                    dependencies = p.Dependencies.Count()
                }).ToList(),
                analyzedAt = result.AnalyzedAt
            };
        }, solutionPath);

    [McpServerTool, Description("Analyzes a specific file and returns detailed information about symbols, diagnostics, and structure.")]
    public Task<string> AnalyzeFile(string filePath) =>
        ExecuteAsync(nameof(AnalyzeFile), async () =>
        {
            var result = await _analysisService.AnalyzeFileAsync(filePath);

            return new
            {
                filePath = result.FilePath,
                projectName = result.ProjectName,
                lineCount = result.LineCount,
                symbolCount = result.Symbols.Count(),
                diagnosticCount = result.Diagnostics.Count(),
                symbols = result.Symbols.Select(s => new
                {
                    name = s.Name,
                    kind = s.Kind.ToString(),
                    signature = s.Signature,
                    location = $"{s.StartLine}:{s.StartColumn}-{s.EndLine}:{s.EndColumn}",
                    isPublic = s.IsPublic,
                    containingNamespace = s.ContainingNamespace,
                    containingType = s.ContainingType
                }).ToList(),
                diagnostics = result.Diagnostics.Select(d => new
                {
                    id = d.Id,
                    severity = d.Severity.ToString(),
                    message = d.GetMessage(),
                    location = d.Location.GetLineSpan().ToString()
                }).ToList(),
                analyzedAt = result.AnalyzedAt
            };
        }, filePath);

    [McpServerTool, Description("Gets all symbols of a specified kind from a project. Optional symbolKind parameter (Class, Method, Property, Field, etc.).")]
    public Task<string> GetSymbols(string projectName, string? symbolKind = null) =>
        ExecuteAsync(nameof(GetSymbols), async () =>
        {
            SymbolKind? kind = null;
            if (!string.IsNullOrEmpty(symbolKind) && Enum.TryParse<SymbolKind>(symbolKind, true, out var parsedKind))
            {
                kind = parsedKind;
            }

            var symbols = await _analysisService.GetSymbolsAsync(projectName, kind);

            return new
            {
                projectName,
                symbolKind = symbolKind ?? "all",
                symbolCount = symbols.Count(),
                symbols = symbols.Select(s => new
                {
                    name = s.Name,
                    kind = s.Kind.ToString(),
                    signature = s.Signature,
                    filePath = s.FilePath,
                    location = $"{s.StartLine}:{s.StartColumn}-{s.EndLine}:{s.EndColumn}",
                    isPublic = s.IsPublic,
                    containingNamespace = s.ContainingNamespace,
                    containingType = s.ContainingType,
                    documentation = string.IsNullOrEmpty(s.Documentation) ? null : s.Documentation
                }).ToList()
            };
        }, projectName, symbolKind);

    [McpServerTool, Description("Finds all references to a symbol across the loaded workspace. Optional projectName to limit scope.")]
    public Task<string> FindReferences(string symbol, string? projectName = null) =>
        ExecuteAsync(nameof(FindReferences), async () =>
        {
            var references = await _analysisService.FindReferencesAsync(symbol, projectName);

            return new
            {
                symbol,
                projectName = projectName ?? "all",
                referenceCount = references.Count(),
                references = references.Select(r => new
                {
                    filePath = r.FilePath,
                    location = $"{r.Line}:{r.Column}",
                    context = r.Context,
                    kind = r.Kind.ToString()
                }).ToList()
            };
        }, symbol, projectName);

    [McpServerTool, Description("Finds all implementations of an interface across the loaded workspace. Optional projectName to limit scope.")]
    public Task<string> FindImplementations(string interfaceName, string? projectName = null) =>
        ExecuteAsync(nameof(FindImplementations), async () =>
        {
            var implementations = await _analysisService.FindImplementationsAsync(interfaceName, projectName);

            return new
            {
                interfaceName,
                projectName = projectName ?? "all",
                implementationCount = implementations.Count(),
                implementations = implementations.Select(impl => new
                {
                    implementingClass = impl.ImplementingClass,
                    implementingClassFullName = impl.ImplementingClassFullName,
                    filePath = impl.FilePath,
                    location = $"{impl.Line}:{impl.Column}",
                    @namespace = impl.Namespace,
                    isAbstract = impl.IsAbstract,
                    isPublic = impl.IsPublic,
                    allImplementedInterfaces = impl.ImplementedInterfaces.ToList()
                }).ToList()
            };
        }, interfaceName, projectName);

    [McpServerTool, Description("Gets compiler diagnostics (errors, warnings, info) for a project or all projects if projectName is not specified.")]
    public Task<string> GetDiagnostics(string? projectName = null) =>
        ExecuteAsync(nameof(GetDiagnostics), async () =>
        {
            var diagnostics = await _workspaceService.GetDiagnosticsAsync(projectName);

            var groupedDiagnostics = diagnostics
                .GroupBy(d => d.Severity)
                .ToDictionary(g => g.Key.ToString(), g => g.ToList());

            return new
            {
                projectName = projectName ?? "all",
                totalCount = diagnostics.Count(),
                summary = new
                {
                    errors = groupedDiagnostics.GetValueOrDefault("Error", []).Count,
                    warnings = groupedDiagnostics.GetValueOrDefault("Warning", []).Count,
                    info = groupedDiagnostics.GetValueOrDefault("Info", []).Count,
                    hidden = groupedDiagnostics.GetValueOrDefault("Hidden", []).Count
                },
                diagnostics = diagnostics.Select(d => new
                {
                    id = d.Id,
                    severity = d.Severity.ToString(),
                    message = d.GetMessage(),
                    location = d.Location.IsInSource ? d.Location.GetLineSpan().ToString() : "External",
                    filePath = d.Location.SourceTree?.FilePath
                }).ToList()
            };
        }, projectName);

    [McpServerTool, Description("Gets the current workspace status including loaded projects and their basic information.")]
    public Task<string> GetWorkspaceStatus() =>
        ExecuteAsync<object>(nameof(GetWorkspaceStatus), async () =>
        {
            var isLoaded = await _workspaceService.IsWorkspaceLoadedAsync();
            var currentSolutionPath = await _workspaceService.GetCurrentSolutionPathAsync();

            if (!isLoaded)
            {
                return new
                {
                    isLoaded = false,
                    solutionPath = currentSolutionPath,
                    message = "No workspace is currently loaded"
                };
            }

            var projects = await _workspaceService.GetProjectsAsync();

            return new
            {
                isLoaded = true,
                solutionPath = currentSolutionPath,
                projectCount = projects.Count(),
                projects = projects.Select(p => new
                {
                    name = p.Name,
                    path = p.FilePath,
                    language = p.Language,
                    documentCount = p.Documents.Count(),
                    hasCompilationErrors = p.GetCompilationAsync().Result?.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error) ?? false
                }).ToList()
            };
        });

    [McpServerTool, Description("Clears the current workspace and invalidates all caches.")]
    public Task<string> ClearWorkspace() =>
        ExecuteAsync(nameof(ClearWorkspace), async () =>
        {
            await _workspaceService.ClearWorkspaceAsync();
            await _analysisService.InvalidateCacheAsync();

            return new
            {
                success = true,
                message = "Workspace cleared and caches invalidated"
            };
        });

    [McpServerTool, Description("Find all callers (controllers/endpoints) that eventually reach the method at the given location.")]
    public Task<string> FindCallersFromLocation(string filePath, int line, int column, int maxDepth = 5, int limit = 100) =>
        ExecuteAsync(nameof(FindCallersFromLocation),
            () => _callGraphService.FindCallersFromLocationAsync(filePath, line, column, maxDepth, limit),
            filePath, line, column, maxDepth, limit);

    [McpServerTool, Description("Find all methods and database operations called from the given location.")]
    public Task<string> FindCalleesFromLocation(string filePath, int line, int column, int maxDepth = 5, int limit = 200) =>
        ExecuteAsync(nameof(FindCalleesFromLocation),
            () => _callGraphService.FindCalleesFromLocationAsync(filePath, line, column, maxDepth, limit),
            filePath, line, column, maxDepth, limit);

    [McpServerTool, Description("Gets MediatR handler mappings showing relationships between requests/commands and their handlers.")]
    public Task<string> GetMediatRMappings() =>
        ExecuteAsync(nameof(GetMediatRMappings), async () =>
        {
            var mappings = await _mediatRMappingService.GetHandlerMappingsAsync();

            return new
            {
                totalMappings = mappings.Count(),
                mappings = mappings.Select(m => new
                {
                    requestType = m.RequestType,
                    requestFullName = m.RequestFullName,
                    handlerType = m.HandlerType,
                    handlerFullName = m.HandlerFullName,
                    responseType = m.ResponseType,
                    handlerLocation = new
                    {
                        filePath = m.HandlerFilePath,
                        line = m.HandlerLine,
                        column = m.HandlerColumn
                    },
                    isCommand = m.IsCommand
                }).ToList()
            };
        });

    [McpServerTool, Description("Echoes the SolutionPath environment variable")]
    public Task<string> EchoSolutionPath() =>
        ExecuteAsync(nameof(EchoSolutionPath), () =>
        {
            var solutionPath = Environment.GetEnvironmentVariable("SolutionPath");
            return Task.FromResult(new { solutionPath = solutionPath ?? "Not set" });
        });
}
