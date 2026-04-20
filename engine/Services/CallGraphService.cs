using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using Engine.Models;
using Engine.Core.Analysis;

namespace Engine.Services;

public class CallGraphService : ICallGraphService
{
    private readonly IRoslynWorkspaceService _workspaceService;
    private readonly IMediatRMappingService _mediatRMappingService;
    private readonly ILogger<CallGraphService> _logger;

    public CallGraphService(
        IRoslynWorkspaceService workspaceService,
        ICacheService cacheService,
        IMediatRMappingService mediatRMappingService,
        ILogger<CallGraphService> logger)
    {
        _workspaceService = workspaceService;
        _mediatRMappingService = mediatRMappingService;
        _logger = logger;
        _ = cacheService;
    }

    // --------------------------------------------------------------------
    // Callers (Roslyn-direct — symbol-finder is already the right tool here)
    // --------------------------------------------------------------------

    public async Task<CallersResult> FindCallersFromLocationAsync(string filePath, int line, int column, int maxDepth = 5, int limit = 100, CancellationToken ct = default)
    {
        var document = await _workspaceService.GetDocumentAsync(filePath, ct);
        if (document is null) return new CallersResult { TargetMethod = "Document not found" };

        var model = await document.GetSemanticModelAsync(ct);
        if (model is null) return new CallersResult { TargetMethod = "Unable to analyze document" };

        var text = await document.GetTextAsync(ct);
        var position = text.Lines[line - 1].Start + column - 1;

        var methodSymbol = await GetMethodSymbolAtPositionAsync(model, position, ct);
        if (methodSymbol is null) return new CallersResult { TargetMethod = "No method found at position" };

        var callers = new List<CallerInfo>();
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        await FindCallersRecursiveAsync(methodSymbol, document.Project.Solution, callers, visited, maxDepth, 0, limit, ct);

        return new CallersResult
        {
            TargetMethod = $"{methodSymbol.ContainingType.Name}.{methodSymbol.Name}",
            Callers = callers.Take(limit).ToList(),
            TotalCallers = callers.Count,
            MaxDepthReached = callers.Any(c => c.Depth >= maxDepth),
            AnalyzedAt = DateTime.UtcNow
        };
    }

    private async Task FindCallersRecursiveAsync(ISymbol target, Solution solution, List<CallerInfo> callers, HashSet<ISymbol> visited, int maxDepth, int currentDepth, int limit, CancellationToken ct)
    {
        if (currentDepth >= maxDepth || callers.Count >= limit || !visited.Add(target)) return;

        var refs = await SymbolFinder.FindReferencesAsync(target, solution, ct);
        foreach (var r in refs)
        {
            foreach (var loc in r.Locations)
            {
                if (callers.Count >= limit) return;

                var doc = solution.GetDocument(loc.Document.Id);
                if (doc is null) continue;
                var model = await doc.GetSemanticModelAsync(ct);
                var root = await doc.GetSyntaxRootAsync(ct);
                if (model is null || root is null) continue;

                var node = root.FindNode(loc.Location.SourceSpan);
                var callingMethodNode = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                if (callingMethodNode is null) continue;

                var callingSymbol = model.GetDeclaredSymbol(callingMethodNode);
                if (callingSymbol is null) continue;

                var span = loc.Location.GetLineSpan();
                callers.Add(new CallerInfo
                {
                    Method = $"{callingSymbol.ContainingType.Name}.{callingSymbol.Name}",
                    File = doc.FilePath ?? "",
                    Line = span.StartLinePosition.Line + 1,
                    Column = span.StartLinePosition.Character + 1,
                    Depth = currentDepth,
                    CallChain = [$"{callingSymbol.ContainingType.Name}.{callingSymbol.Name} → {target.ContainingType.Name}.{target.Name}"],
                    EndpointInfo = GetEndpointInfo(callingSymbol)
                });

                await FindCallersRecursiveAsync(callingSymbol, solution, callers, visited, maxDepth, currentDepth + 1, limit, ct);
            }
        }
    }

    // --------------------------------------------------------------------
    // Callees — thin shell over Engine.Core.Analysis.CallGraphBuilder.
    // The Roslyn side's only job is building the facts adapter and
    // translating the pure TraversalResult back into the legacy DTOs.
    // --------------------------------------------------------------------

    public async Task<CalleesResult> FindCalleesFromLocationAsync(string filePath, int line, int column, int maxDepth = 5, int limit = 200, CancellationToken ct = default)
    {
        await _mediatRMappingService.BuildMappingsAsync(ct);

        var document = await _workspaceService.GetDocumentAsync(filePath, ct);
        if (document is null) return new CalleesResult { SourceMethod = "Document not found" };

        var model = await document.GetSemanticModelAsync(ct);
        if (model is null) return new CalleesResult { SourceMethod = "Unable to analyze document" };

        var text = await document.GetTextAsync(ct);
        var position = text.Lines[line - 1].Start + column - 1;

        var methodSymbol = await GetMethodSymbolAtPositionAsync(model, position, ct);
        if (methodSymbol is null) return new CalleesResult { SourceMethod = "No method found at position" };

        var compilation = model.Compilation;
        var facts = new RoslynAnalysisFacts(document.Project.Solution, compilation, _mediatRMappingService, ct);
        var entryId = facts.Register(methodSymbol);

        var result = new CallGraphBuilder(facts)
            .Build(entryId, new TraversalOptions(MaxDepth: maxDepth, Limit: limit));

        var callees = result.Callees
            .Select(c => new CalleeInfo
            {
                Method = $"{c.Target.ContainingType.Name}.{c.Target.Name}",
                File = c.Location.FilePath,
                Line = c.Location.Line,
                Column = c.Location.Column,
                CallType = c.Kind switch
                {
                    CallKind.MediatR => "MediatR",
                    CallKind.Database => "Database",
                    _ => "Method"
                },
                TargetHandler = c.HandlerName,
                Operation = c.Operation,
                Entity = c.Entity?.DisplayName,
                Depth = c.Depth
            })
            .ToList();

        var dbOps = result.DatabaseOperations
            .Select(c => new DatabaseOperation
            {
                Operation = c.Operation ?? "QUERY",
                Table = c.Entity?.DisplayName ?? "",
                Type = c.IsWrite ? "Write" : "Read",
                Location = $"line {c.Location.Line}",
                Method = c.Target.Name
            })
            .ToList();

        var externalCalls = result.MediatRSends
            .GroupBy(c => c.HandlerName ?? "Send")
            .Select(g => new ExternalCall
            {
                Service = "MediatR",
                Type = "MediatR",
                Operations = [g.Key],
                Locations = g.Select(c => $"line {c.Location.Line}").Distinct().ToList()
            })
            .ToList();

        return new CalleesResult
        {
            SourceMethod = $"{methodSymbol.ContainingType.Name}.{methodSymbol.Name}",
            Callees = callees,
            DatabaseOperations = dbOps,
            ExternalCalls = externalCalls,
            TotalCallees = callees.Count,
            MaxDepthReached = result.MaxDepthReached,
            AnalyzedAt = DateTime.UtcNow
        };
    }

    // --------------------------------------------------------------------
    // Misc helpers
    // --------------------------------------------------------------------

    private static async Task<IMethodSymbol?> GetMethodSymbolAtPositionAsync(SemanticModel model, int position, CancellationToken ct)
    {
        var root = await model.SyntaxTree.GetRootAsync(ct);
        var node = root.FindToken(position).Parent;
        while (node is not null)
        {
            switch (node)
            {
                case MethodDeclarationSyntax m: return model.GetDeclaredSymbol(m) as IMethodSymbol;
                case LocalFunctionStatementSyntax lf: return model.GetDeclaredSymbol(lf) as IMethodSymbol;
                case AccessorDeclarationSyntax acc: return model.GetDeclaredSymbol(acc) as IMethodSymbol;
            }
            node = node.Parent;
        }
        return null;
    }

    private static EndpointInfo? GetEndpointInfo(ISymbol methodSymbol)
    {
        if (methodSymbol is not IMethodSymbol method) return null;
        var containingType = method.ContainingType;
        if (!containingType.Name.EndsWith("Controller") || containingType.BaseType?.Name != "Controller")
            return null;

        return new EndpointInfo
        {
            IsController = true,
            ControllerName = containingType.Name.Replace("Controller", ""),
            ActionName = method.Name,
            HttpMethod = ExtractHttpMethod(method),
            Route = $"/{containingType.Name.Replace("Controller", "").ToLowerInvariant()}/{method.Name.ToLowerInvariant()}"
        };
    }

    private static string ExtractHttpMethod(IMethodSymbol method)
    {
        foreach (var attr in method.GetAttributes())
        {
            var name = attr.AttributeClass?.Name;
            if (name?.EndsWith("Attribute") == true) name = name[..^9];
            return name switch
            {
                "HttpGet" or "Get" => "GET",
                "HttpPost" or "Post" => "POST",
                "HttpPut" or "Put" => "PUT",
                "HttpDelete" or "Delete" => "DELETE",
                "HttpPatch" or "Patch" => "PATCH",
                _ => "GET"
            };
        }
        return "GET";
    }
}
