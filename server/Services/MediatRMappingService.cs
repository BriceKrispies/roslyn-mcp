using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace mcp_server.Services;

public class MediatRMappingService : IMediatRMappingService
{
    private readonly IRoslynWorkspaceService _workspaceService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<MediatRMappingService> _logger;
    private readonly ConcurrentDictionary<string, HandlerMapping> _mappings = new();
    private bool _isMappingBuilt = false;

    public MediatRMappingService(
        IRoslynWorkspaceService workspaceService,
        ICacheService cacheService,
        ILogger<MediatRMappingService> logger)
    {
        _workspaceService = workspaceService;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task BuildMappingsAsync(CancellationToken cancellationToken = default)
    {
        if (_isMappingBuilt)
        {
            _logger.LogDebug("MediatR mappings already built");
            return;
        }

        _logger.LogInformation("Building MediatR handler mappings...");

        var cacheKey = "mediatr_mappings_v1";
        var cachedMappings = await _cacheService.GetAsync<List<HandlerMapping>>(cacheKey, cancellationToken);
        
        if (cachedMappings != null)
        {
            _logger.LogDebug("Loading MediatR mappings from cache");
            foreach (var mapping in cachedMappings)
            {
                _mappings.TryAdd(mapping.RequestType, mapping);
            }
            _isMappingBuilt = true;
            return;
        }

        var projects = await _workspaceService.GetProjectsAsync(cancellationToken);
        var allMappings = new List<HandlerMapping>();

        foreach (var project in projects)
        {
            try
            {
                var projectMappings = await AnalyzeProjectForMediatRAsync(project, cancellationToken);
                allMappings.AddRange(projectMappings);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze project for MediatR mappings: {ProjectName}", project.Name);
            }
        }

        // Build the mapping dictionary
        foreach (var mapping in allMappings)
        {
            _mappings.TryAdd(mapping.RequestType, mapping);
        }

        // Cache the results
        await _cacheService.SetAsync(cacheKey, allMappings, TimeSpan.FromHours(2), cancellationToken);
        
        _isMappingBuilt = true;
        _logger.LogInformation("Built {MappingCount} MediatR handler mappings", _mappings.Count);
    }

    public async Task<IEnumerable<HandlerMapping>> GetHandlerMappingsAsync(CancellationToken cancellationToken = default)
    {
        await BuildMappingsAsync(cancellationToken);
        return _mappings.Values;
    }

    public async Task<HandlerMapping?> FindHandlerForRequestAsync(string requestTypeName, CancellationToken cancellationToken = default)
    {
        await BuildMappingsAsync(cancellationToken);
        
        _mappings.TryGetValue(requestTypeName, out var mapping);
        return mapping;
    }

    public async Task<IEnumerable<string>> FindRequestsForHandlerAsync(string handlerTypeName, CancellationToken cancellationToken = default)
    {
        await BuildMappingsAsync(cancellationToken);
        
        return _mappings.Values
            .Where(m => m.HandlerType == handlerTypeName)
            .Select(m => m.RequestType);
    }

    private async Task<List<HandlerMapping>> AnalyzeProjectForMediatRAsync(Project project, CancellationToken cancellationToken)
    {
        var mappings = new List<HandlerMapping>();
        var compilation = await project.GetCompilationAsync(cancellationToken);
        
        if (compilation == null)
        {
            _logger.LogWarning("No compilation available for project: {ProjectName}", project.Name);
            return mappings;
        }

        _logger.LogDebug("Analyzing project {ProjectName} for MediatR handlers", project.Name);

        // Get all types from source in this project (not metadata types)
        var sourceTypes = new List<INamedTypeSymbol>();
        
        foreach (var document in project.Documents)
        {
            try
            {
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (semanticModel?.SyntaxTree != null)
                {
                    var root = await semanticModel.SyntaxTree.GetRootAsync(cancellationToken);
                    var classDeclarations = root.DescendantNodes()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>();
                    
                    foreach (var classDecl in classDeclarations)
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
                        if (symbol != null)
                        {
                            sourceTypes.Add(symbol);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze document: {DocumentName}", document.Name);
            }
        }

        _logger.LogDebug("Found {TypeCount} source types in project {ProjectName}", sourceTypes.Count, project.Name);

        // Find all handler implementations
        var handlers = sourceTypes.Where(IsRequestHandler).ToList();
        
        _logger.LogInformation("Found {HandlerCount} MediatR handlers in project {ProjectName}", handlers.Count, project.Name);

        foreach (var handler in handlers)
        {
            try
            {
                _logger.LogDebug("Processing handler: {HandlerName}", handler.Name);
                var mapping = await CreateHandlerMappingAsync(handler, project, cancellationToken);
                if (mapping != null)
                {
                    mappings.Add(mapping);
                    _logger.LogInformation("Mapped {RequestType} → {HandlerType}", mapping.RequestType, mapping.HandlerType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create mapping for handler: {HandlerName}", handler.Name);
            }
        }

        return mappings;
    }

    private bool IsRequestHandler(INamedTypeSymbol type)
    {
        var interfaces = type.AllInterfaces.ToList();
        _logger.LogDebug("Checking type {TypeName} with {InterfaceCount} interfaces", type.Name, interfaces.Count);
        
        foreach (var iface in interfaces)
        {
            _logger.LogDebug("  Interface: {InterfaceName} in namespace {Namespace}", 
                iface.Name, iface.ContainingNamespace?.ToDisplayString());
        }
        
        var isHandler = interfaces.Any(i => 
            i.Name == "IRequestHandler" && 
            i.ContainingNamespace?.ToDisplayString() == "MediatR");
            
        _logger.LogDebug("Type {TypeName} is handler: {IsHandler}", type.Name, isHandler);
        return isHandler;
    }

    private async Task<HandlerMapping?> CreateHandlerMappingAsync(INamedTypeSymbol handlerType, Project project, CancellationToken cancellationToken)
    {
        // Find the IRequestHandler<TRequest, TResponse> interface
        var requestHandlerInterface = handlerType.AllInterfaces.FirstOrDefault(i =>
            i.Name == "IRequestHandler" && 
            i.ContainingNamespace?.ToDisplayString() == "MediatR");

        if (requestHandlerInterface == null || requestHandlerInterface.TypeArguments.Length == 0)
        {
            return null;
        }

        var requestType = requestHandlerInterface.TypeArguments[0];
        var responseType = requestHandlerInterface.TypeArguments.Length > 1 
            ? requestHandlerInterface.TypeArguments[1] 
            : null;

        // Find the Handle method location
        var (filePath, line, column) = await FindHandleMethodLocationAsync(handlerType, project, cancellationToken);

        return new HandlerMapping
        {
            RequestType = requestType.Name,
            RequestFullName = requestType.ToDisplayString(),
            HandlerType = handlerType.Name,
            HandlerFullName = handlerType.ToDisplayString(),
            ResponseType = responseType?.Name ?? "void",
            HandlerMethodName = "Handle",
            HandlerFilePath = filePath,
            HandlerLine = line,
            HandlerColumn = column,
            IsCommand = responseType == null
        };
    }

    private async Task<(string filePath, int line, int column)> FindHandleMethodLocationAsync(INamedTypeSymbol handlerType, Project project, CancellationToken cancellationToken)
    {
        var syntaxReferences = handlerType.DeclaringSyntaxReferences;
        
        foreach (var syntaxRef in syntaxReferences)
        {
            var document = project.GetDocument(syntaxRef.SyntaxTree);
            if (document == null) continue;

            var root = await syntaxRef.GetSyntaxAsync(cancellationToken);
            var classDeclaration = root.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                .FirstOrDefault();

            if (classDeclaration == null) continue;

            // Find the Handle method
            var handleMethod = classDeclaration.Members
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.ValueText == "Handle");

            if (handleMethod != null)
            {
                var location = handleMethod.GetLocation().GetLineSpan();
                return (
                    document.FilePath ?? "",
                    location.StartLinePosition.Line + 1,
                    location.StartLinePosition.Character + 1
                );
            }
        }

        return ("", 0, 0);
    }
}
