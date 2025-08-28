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
        return await ResponseBuilder
            .ForOperation("LoadSolution", _logger)
            .WithParameters(solutionPath)
            .ExecuteAsync<object>(async () =>
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
            })
            .ToJsonAsync();
    }

    [McpServerTool, Description("Loads a single .NET project and prepares it for analysis.")]
    public async Task<string> LoadProject(string projectPath)
    {
        return await ResponseBuilder
            .ForOperation("LoadProject", _logger)
            .WithParameters(projectPath)
            .ExecuteAsync<object>(async () =>
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
            })
            .ToJsonAsync();
    }

    [McpServerTool, Description("Analyzes a project and returns comprehensive analysis results including diagnostics, symbols, and metrics.")]
    public async Task<string> AnalyzeProject(string projectPath)
    {
        return await ResponseBuilder
            .ForOperation("AnalyzeProject", _logger)
            .WithParameters(projectPath)
            .ExecuteAsync<object>(async () =>
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
            })
            .ToJsonAsync();
    }

    [McpServerTool, Description("Analyzes a solution and returns analysis for all projects within it.")]
    public async Task<string> AnalyzeSolution(string solutionPath)
    {
        return await ResponseBuilder
            .ForOperation("AnalyzeSolution", _logger)
            .WithParameters(solutionPath)
            .ExecuteAsync<object>(async () =>
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
            })
            .ToJsonAsync();
    }

    [McpServerTool, Description("Analyzes a specific file and returns detailed information about symbols, diagnostics, and structure.")]
    public async Task<string> AnalyzeFile(string filePath)
    {
        return await ResponseBuilder
            .ForOperation("AnalyzeFile", _logger)
            .WithParameters(filePath)
            .ExecuteAsync<object>(async () =>
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
            })
            .ToJsonAsync();
    }

    [McpServerTool, Description("Gets all symbols of a specified kind from a project. Optional symbolKind parameter (Class, Method, Property, Field, etc.).")]
    public async Task<string> GetSymbols(string projectName, string? symbolKind = null)
    {
        return await ResponseBuilder
            .ForOperation("GetSymbols", _logger)
            .WithParameters(projectName, symbolKind)
            .ExecuteAsync<object>(async () =>
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
            })
            .ToJsonAsync();
    }

    [McpServerTool, Description("Finds all references to a symbol across the loaded workspace. Optional projectName to limit scope.")]
    public async Task<string> FindReferences(string symbol, string? projectName = null)
    {
        return await ResponseBuilder
            .ForOperation("FindReferences", _logger)
            .WithParameters(symbol, projectName)
            .ExecuteAsync<object>(async () =>
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
            })
            .ToJsonAsync();
    }

    [McpServerTool, Description("Finds all implementations of an interface across the loaded workspace. Optional projectName to limit scope.")]
    public async Task<string> FindImplementations(string interfaceName, string? projectName = null)
    {
        return await ResponseBuilder
            .ForOperation("FindImplementations", _logger)
            .WithParameters(interfaceName, projectName)
            .ExecuteAsync<object>(async () =>
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
            })
            .ToJsonAsync();
    }

    [McpServerTool, Description("Gets compiler diagnostics (errors, warnings, info) for a project or all projects if projectName is not specified.")]
    public async Task<string> GetDiagnostics(string? projectName = null)
    {
        return await ResponseBuilder
            .ForOperation("GetDiagnostics", _logger)
            .WithParameters(projectName)
            .ExecuteAsync<object>(async () =>
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
            })
            .ToJsonAsync();
    }

    [McpServerTool, Description("Gets the current workspace status including loaded projects and their basic information.")]
    public async Task<string> GetWorkspaceStatus()
    {
        return await ResponseBuilder
            .ForOperation("GetWorkspaceStatus", _logger)
            .ExecuteAsync<object>(async () =>
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
            })
            .ToJsonAsync();
    }

    [McpServerTool, Description("Clears the current workspace and invalidates all caches.")]
    public async Task<string> ClearWorkspace()
    {
        return await ResponseBuilder
            .ForOperation("ClearWorkspace", _logger)
            .ExecuteAsync<object>(async () =>
            {
                await _workspaceService.ClearWorkspaceAsync();
                await _analysisService.InvalidateCacheAsync();
                
                return new
                {
                    success = true,
                    message = "Workspace cleared and caches invalidated"
                };
            })
            .ToJsonAsync();
    }

    [McpServerTool, Description("Find all callers (controllers/endpoints) that eventually reach the method at the given location.")]
    public async Task<string> FindCallersFromLocation(string filePath, int line, int column, int maxDepth = 5, int limit = 100)
    {
        return await ResponseBuilder
            .ForOperation("FindCallersFromLocation", _logger)
            .WithParameters(filePath, line, column, maxDepth, limit)
            .ExecuteAsync(() => _callGraphService.FindCallersFromLocationAsync(filePath, line, column, maxDepth, limit))
            .ToJsonAsync();
    }

    [McpServerTool, Description("Find all methods and database operations called from the given location.")]
    public async Task<string> FindCalleesFromLocation(string filePath, int line, int column, int maxDepth = 5, int limit = 200)
    {
        return await ResponseBuilder
            .ForOperation("FindCalleesFromLocation", _logger)
            .WithParameters(filePath, line, column, maxDepth, limit)
            .ExecuteAsync(() => _callGraphService.FindCalleesFromLocationAsync(filePath, line, column, maxDepth, limit))
            .ToJsonAsync();
    }

    [McpServerTool, Description("Gets MediatR handler mappings showing relationships between requests/commands and their handlers.")]
    public async Task<string> GetMediatRMappings()
    {
        return await ResponseBuilder
            .ForOperation("GetMediatRMappings", _logger)
            .ExecuteAsync<object>(async () =>
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
            })
            .ToJsonAsync();
    }

    [McpServerTool, Description("Echoes the SolutionPath environment variable")]
    public async Task<string> EchoSolutionPath()
    {
        return await ResponseBuilder
            .ForOperation("EchoSolutionPath", _logger)
            .ExecuteAsync<object>(async () =>
            {
                await Task.CompletedTask; // Make it async for consistency
                var solutionPath = Environment.GetEnvironmentVariable("SolutionPath");
                return new { solutionPath = solutionPath ?? "Not set" };
            })
            .ToJsonAsync();
    }
}

/// <summary>
/// Builder pattern for creating MCP tool responses with consistent error handling and logging
/// </summary>
public class ResponseBuilder
{
    private readonly ILogger _logger;
    private readonly string _operationName;
    private object?[]? _parameters;
    private Func<Task<object>>? _operation;
    private readonly List<Func<Exception, Task<bool>>> _errorHandlers = new();
    private bool _includeStackTrace = false;
    private Formatting _jsonFormatting = Formatting.Indented;

    private ResponseBuilder(string operationName, ILogger logger)
    {
        _operationName = operationName;
        _logger = logger;
    }

    public static ResponseBuilder ForOperation(string operationName, ILogger logger)
    {
        return new ResponseBuilder(operationName, logger);
    }

    public ResponseBuilder WithParameters(params object?[] parameters)
    {
        _parameters = parameters;
        return this;
    }

    public ResponseBuilder ExecuteAsync<T>(Func<Task<T>> operation)
    {
        _operation = async () => (object)(await operation())!;
        return this;
    }

    public ResponseBuilder OnError(Func<Exception, Task<bool>> errorHandler)
    {
        _errorHandlers.Add(errorHandler);
        return this;
    }

    public ResponseBuilder IncludeStackTrace(bool include = true)
    {
        _includeStackTrace = include;
        return this;
    }

    public ResponseBuilder WithJsonFormatting(Formatting formatting)
    {
        _jsonFormatting = formatting;
        return this;
    }

    public async Task<string> ToJsonAsync()
    {
        try
        {
            _logger.LogInformation("Executing operation: {OperationName} with parameters: {Parameters}", 
                _operationName, 
                _parameters != null ? string.Join(", ", _parameters.Select(p => p?.ToString() ?? "null")) : "none");

            if (_operation == null)
            {
                throw new InvalidOperationException("No operation specified. Call ExecuteAsync() first.");
            }

            var result = await _operation();
            
            _logger.LogDebug("Operation {OperationName} completed successfully", _operationName);
            
            return JsonConvert.SerializeObject(result, _jsonFormatting);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in operation: {OperationName}", _operationName);

            // Try custom error handlers first
            foreach (var handler in _errorHandlers)
            {
                if (await handler(ex))
                {
                    // Handler indicated it processed the error
                    return JsonConvert.SerializeObject(new { success = false, error = "Handled by custom error handler" }, _jsonFormatting);
                }
            }

            // Default error response
            var errorResponse = new
            {
                success = false,
                error = ex.Message,
                operationName = _operationName,
                stackTrace = _includeStackTrace ? ex.StackTrace : null
            };

            return JsonConvert.SerializeObject(errorResponse, _jsonFormatting);
        }
    }
}