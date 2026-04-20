using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace Engine.Services;

public class MediatRMappingService : IMediatRMappingService
{
    private readonly IRoslynWorkspaceService _workspaceService;
    private readonly ILogger<MediatRMappingService> _logger;

    private readonly Dictionary<INamedTypeSymbol, HandlerMapping> _byRequestSymbol =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<string, HandlerMapping> _byRequestFullName = new();
    private bool _isMappingBuilt;

    public MediatRMappingService(
        IRoslynWorkspaceService workspaceService,
        ILogger<MediatRMappingService> logger)
    {
        _workspaceService = workspaceService;
        _logger = logger;
    }

    public async Task BuildMappingsAsync(CancellationToken ct = default)
    {
        if (_isMappingBuilt) return;

        var projects = await _workspaceService.GetProjectsAsync(ct);

        foreach (var project in projects)
        {
            try
            {
                await ScanProjectAsync(project, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan {Project} for MediatR handlers", project.Name);
            }
        }

        _isMappingBuilt = true;
        _logger.LogInformation("Built {Count} MediatR handler mappings", _byRequestSymbol.Count);
    }

    public async Task RebuildMappingsAsync(CancellationToken ct = default)
    {
        _isMappingBuilt = false;
        _byRequestSymbol.Clear();
        _byRequestFullName.Clear();
        await BuildMappingsAsync(ct);
    }

    public async Task<IEnumerable<HandlerMapping>> GetHandlerMappingsAsync(CancellationToken ct = default)
    {
        await BuildMappingsAsync(ct);
        return _byRequestSymbol.Values.ToList();
    }

    public async Task<HandlerMapping?> FindHandlerForRequestAsync(string requestTypeName, CancellationToken ct = default)
    {
        await BuildMappingsAsync(ct);
        if (_byRequestFullName.TryGetValue(requestTypeName, out var byFull)) return byFull;
        return _byRequestSymbol.Values.FirstOrDefault(m => m.RequestType == requestTypeName);
    }

    public async Task<HandlerMapping?> FindHandlerForRequestSymbolAsync(ITypeSymbol requestType, CancellationToken ct = default)
    {
        await BuildMappingsAsync(ct);
        if (requestType is INamedTypeSymbol named && _byRequestSymbol.TryGetValue(named, out var bySym))
            return bySym;
        return _byRequestFullName.TryGetValue(requestType.ToDisplayString(), out var byFull) ? byFull : null;
    }

    public async Task<IEnumerable<string>> FindRequestsForHandlerAsync(string handlerTypeName, CancellationToken ct = default)
    {
        await BuildMappingsAsync(ct);
        return _byRequestSymbol.Values
            .Where(m => m.HandlerType == handlerTypeName || m.HandlerFullName == handlerTypeName)
            .Select(m => m.RequestType)
            .ToList();
    }

    private async Task ScanProjectAsync(Project project, CancellationToken ct)
    {
        var compilation = await project.GetCompilationAsync(ct);
        if (compilation is null) return;

        var symbols = MediatRSymbols.From(compilation);
        if (symbols is null) return;

        foreach (var doc in project.Documents)
        {
            var model = await doc.GetSemanticModelAsync(ct);
            if (model?.SyntaxTree is null) continue;

            var root = await model.SyntaxTree.GetRootAsync(ct);
            foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol type) continue;
                if (type.IsAbstract) continue;

                var iface = FindHandlerInterface(type, symbols);
                if (iface is null || iface.TypeArguments.Length == 0) continue;
                if (iface.TypeArguments[0] is not INamedTypeSymbol requestType) continue;

                var mapping = BuildMapping(type, requestType, iface, classDecl, doc);
                _byRequestSymbol[requestType] = mapping;
                _byRequestFullName[mapping.RequestFullName] = mapping;
            }
        }
    }

    private static INamedTypeSymbol? FindHandlerInterface(INamedTypeSymbol type, MediatRSymbols symbols)
    {
        foreach (var iface in type.AllInterfaces)
        {
            var open = iface.OriginalDefinition;
            if (SymbolEqualityComparer.Default.Equals(open, symbols.IRequestHandler2) ||
                SymbolEqualityComparer.Default.Equals(open, symbols.IRequestHandler1))
            {
                return iface;
            }
        }
        return null;
    }

    private static HandlerMapping BuildMapping(
        INamedTypeSymbol handlerType,
        INamedTypeSymbol requestType,
        INamedTypeSymbol handlerInterface,
        ClassDeclarationSyntax classDecl,
        Document doc)
    {
        var responseType = handlerInterface.TypeArguments.Length > 1
            ? handlerInterface.TypeArguments[1]
            : null;

        var (filePath, line, column) = LocateHandleMethod(handlerType, classDecl, doc);

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
            IsCommand = responseType is null
        };
    }

    private static (string filePath, int line, int column) LocateHandleMethod(
        INamedTypeSymbol handlerType, ClassDeclarationSyntax classDecl, Document doc)
    {
        var handle = classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "Handle");

        if (handle is not null)
        {
            var span = handle.GetLocation().GetLineSpan();
            var path = doc.FilePath ?? handle.SyntaxTree.FilePath ?? "";
            return (path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
        }

        var loc = handlerType.Locations.FirstOrDefault(l => l.IsInSource);
        if (loc is null) return ("", 0, 0);
        var ls = loc.GetLineSpan();
        return (ls.Path, ls.StartLinePosition.Line + 1, ls.StartLinePosition.Character + 1);
    }
}
