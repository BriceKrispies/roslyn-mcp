using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using mcp_server.Services;
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

    [McpServerTool, Description("Loads a .NET solution and prepares it for analysis. Returns information about loaded projects.")]
    public async Task<string> LoadSolution(string solutionPath)
    {
        try
        {
            _logger.LogInformation("Loading solution: {SolutionPath}", solutionPath);
            
            var success = await _workspaceService.LoadSolutionAsync(solutionPath);
            if (!success)
            {
                return JsonConvert.SerializeObject(new { success = false, error = "Failed to load solution" });
            }

            var projects = await _workspaceService.GetProjectsAsync();
            var projectInfo = projects.Select(p => new
            {
                name = p.Name,
                path = p.FilePath,
                language = p.Language,
                documentCount = p.Documents.Count()
            }).ToList();

            return JsonConvert.SerializeObject(new
            {
                success = true,
                solutionPath,
                projectCount = projectInfo.Count,
                projects = projectInfo
            }, Formatting.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading solution: {SolutionPath}", solutionPath);
            return JsonConvert.SerializeObject(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool, Description("Loads a single .NET project and prepares it for analysis.")]
    public async Task<string> LoadProject(string projectPath)
    {
        try
        {
            _logger.LogInformation("Loading project: {ProjectPath}", projectPath);
            
            var success = await _workspaceService.LoadProjectAsync(projectPath);
            if (!success)
            {
                return JsonConvert.SerializeObject(new { success = false, error = "Failed to load project" });
            }

            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var project = await _workspaceService.GetProjectByNameAsync(projectName);
            
            if (project == null)
            {
                return JsonConvert.SerializeObject(new { success = false, error = "Project not found after loading" });
            }

            return JsonConvert.SerializeObject(new
            {
                success = true,
                projectName = project.Name,
                projectPath,
                language = project.Language,
                documentCount = project.Documents.Count(),
                documents = project.Documents.Select(d => d.Name).ToList()
            }, Formatting.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading project: {ProjectPath}", projectPath);
            return JsonConvert.SerializeObject(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool, Description("Analyzes a project and returns comprehensive analysis results including diagnostics, symbols, and metrics.")]
    public async Task<string> AnalyzeProject(string projectPath)
    {
        try
        {
            _logger.LogInformation("Analyzing project: {ProjectPath}", projectPath);
            
            var result = await _analysisService.AnalyzeProjectAsync(projectPath);
            
            return JsonConvert.SerializeObject(new
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
            }, Formatting.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing project: {ProjectPath}", projectPath);
            return JsonConvert.SerializeObject(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool, Description("Analyzes a solution and returns analysis for all projects within it.")]
    public async Task<string> AnalyzeSolution(string solutionPath)
    {
        try
        {
            _logger.LogInformation("Analyzing solution: {SolutionPath}", solutionPath);
            
            var result = await _analysisService.AnalyzeSolutionAsync(solutionPath);
            
            return JsonConvert.SerializeObject(new
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
            }, Formatting.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing solution: {SolutionPath}", solutionPath);
            return JsonConvert.SerializeObject(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool, Description("Analyzes a specific file and returns detailed information about symbols, diagnostics, and structure.")]
    public async Task<string> AnalyzeFile(string filePath)
    {
        try
        {
            _logger.LogInformation("Analyzing file: {FilePath}", filePath);
            
            var result = await _analysisService.AnalyzeFileAsync(filePath);
            
            return JsonConvert.SerializeObject(new
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
            }, Formatting.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing file: {FilePath}", filePath);
            return JsonConvert.SerializeObject(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool, Description("Gets all symbols of a specified kind from a project. Optional symbolKind parameter (Class, Method, Property, Field, etc.).")]
    public async Task<string> GetSymbols(string projectName, string? symbolKind = null)
    {
        try
        {
            _logger.LogInformation("Getting symbols for project: {ProjectName}, kind: {SymbolKind}", projectName, symbolKind ?? "all");
            
            SymbolKind? kind = null;
            if (!string.IsNullOrEmpty(symbolKind) && Enum.TryParse<SymbolKind>(symbolKind, true, out var parsedKind))
            {
                kind = parsedKind;
            }

            var symbols = await _analysisService.GetSymbolsAsync(projectName, kind);
            
            return JsonConvert.SerializeObject(new
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
            }, Formatting.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting symbols for project: {ProjectName}", projectName);
            return JsonConvert.SerializeObject(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool, Description("Finds all references to a symbol across the loaded workspace. Optional projectName to limit scope.")]
    public async Task<string> FindReferences(string symbol, string? projectName = null)
    {
        try
        {
            _logger.LogInformation("Finding references for symbol: {Symbol} in project: {ProjectName}", symbol, projectName ?? "all");
            
            var references = await _analysisService.FindReferencesAsync(symbol, projectName);
            
            return JsonConvert.SerializeObject(new
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
            }, Formatting.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding references for symbol: {Symbol}", symbol);
            return JsonConvert.SerializeObject(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool, Description("Finds all implementations of an interface across the loaded workspace. Optional projectName to limit scope.")]
    public async Task<string> FindImplementations(string interfaceName, string? projectName = null)
    {
        try
        {
            _logger.LogInformation("Finding implementations for interface: {InterfaceName} in project: {ProjectName}", interfaceName, projectName ?? "all");
            
            var implementations = await _analysisService.FindImplementationsAsync(interfaceName, projectName);
            
            return JsonConvert.SerializeObject(new
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
            }, Formatting.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding implementations for interface: {InterfaceName}", interfaceName);
            return JsonConvert.SerializeObject(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool, Description("Gets compiler diagnostics (errors, warnings, info) for a project or all projects if projectName is not specified.")]
    public async Task<string> GetDiagnostics(string? projectName = null)
    {
        try
        {
            _logger.LogInformation("Getting diagnostics for project: {ProjectName}", projectName ?? "all");
            
            var diagnostics = await _workspaceService.GetDiagnosticsAsync(projectName);
            
            var groupedDiagnostics = diagnostics
                .GroupBy(d => d.Severity)
                .ToDictionary(g => g.Key.ToString(), g => g.ToList());

            return JsonConvert.SerializeObject(new
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
            }, Formatting.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting diagnostics for project: {ProjectName}", projectName);
            return JsonConvert.SerializeObject(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool, Description("Gets the current workspace status including loaded projects and their basic information.")]
    public async Task<string> GetWorkspaceStatus()
    {
        try
        {
            var isLoaded = await _workspaceService.IsWorkspaceLoadedAsync();
            var currentSolutionPath = await _workspaceService.GetCurrentSolutionPathAsync();
            
            if (!isLoaded)
            {
                return JsonConvert.SerializeObject(new
                {
                    isLoaded = false,
                    solutionPath = currentSolutionPath,
                    message = "No workspace is currently loaded"
                });
            }

            var projects = await _workspaceService.GetProjectsAsync();
            
            return JsonConvert.SerializeObject(new
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
            }, Formatting.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting workspace status");
            return JsonConvert.SerializeObject(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool, Description("Clears the current workspace and invalidates all caches.")]
    public async Task<string> ClearWorkspace()
    {
        try
        {
            _logger.LogInformation("Clearing workspace");
            
            await _workspaceService.ClearWorkspaceAsync();
            await _analysisService.InvalidateCacheAsync();
            
            return JsonConvert.SerializeObject(new
            {
                success = true,
                message = "Workspace cleared and caches invalidated"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing workspace");
            return JsonConvert.SerializeObject(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool, Description("Find all callers (controllers/endpoints) that eventually reach the method at the given location.")]
    public async Task<string> FindCallersFromLocation(string filePath, int line, int column, int maxDepth = 5, int limit = 100)
    {
        try
        {
            _logger.LogInformation("Finding callers from {FilePath}:{Line}:{Column}", filePath, line, column);
            
            var result = await _callGraphService.FindCallersFromLocationAsync(filePath, line, column, maxDepth, limit);
            
            return JsonConvert.SerializeObject(result, Formatting.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding callers from location: {FilePath}:{Line}:{Column}", filePath, line, column);
            return JsonConvert.SerializeObject(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool, Description("Find all methods and database operations called from the given location.")]
    public async Task<string> FindCalleesFromLocation(string filePath, int line, int column, int maxDepth = 5, int limit = 200)
    {
        try
        {
            _logger.LogInformation("Finding callees from {FilePath}:{Line}:{Column}", filePath, line, column);
            
            var result = await _callGraphService.FindCalleesFromLocationAsync(filePath, line, column, maxDepth, limit);
            
            return JsonConvert.SerializeObject(result, Formatting.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding callees from location: {FilePath}:{Line}:{Column}", filePath, line, column);
            return JsonConvert.SerializeObject(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool, Description("Gets MediatR handler mappings showing relationships between requests/commands and their handlers.")]
    public async Task<string> GetMediatRMappings()
    {
        try
        {
            _logger.LogInformation("Getting MediatR handler mappings");
            
            var mappings = await _mediatRMappingService.GetHandlerMappingsAsync();
            
            return JsonConvert.SerializeObject(new
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
            }, Formatting.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting MediatR mappings");
            return JsonConvert.SerializeObject(new { success = false, error = ex.Message });
        }
    }
}
