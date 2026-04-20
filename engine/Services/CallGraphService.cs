using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using Engine.Models;

namespace Engine.Services;

public class CallGraphService : ICallGraphService
{
    private readonly IRoslynWorkspaceService _workspaceService;
    private readonly IMediatRMappingService _mediatRMappingService;
    private readonly ILogger<CallGraphService> _logger;

    private readonly Dictionary<Compilation, MediatRSymbols?> _mediatRCache = new();
    private readonly Dictionary<Compilation, EfCoreSymbols?> _efCoreCache = new();

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
    // Callers (unchanged in shape; uses Roslyn's SymbolFinder)
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
    // Callees (rewritten — symbol-based classification)
    // --------------------------------------------------------------------

    public async Task<CalleesResult> FindCalleesFromLocationAsync(string filePath, int line, int column, int maxDepth = 5, int limit = 200, CancellationToken ct = default)
    {
        _mediatRCache.Clear();
        _efCoreCache.Clear();

        await _mediatRMappingService.BuildMappingsAsync(ct);

        var document = await _workspaceService.GetDocumentAsync(filePath, ct);
        if (document is null) return new CalleesResult { SourceMethod = "Document not found" };

        var model = await document.GetSemanticModelAsync(ct);
        if (model is null) return new CalleesResult { SourceMethod = "Unable to analyze document" };

        var text = await document.GetTextAsync(ct);
        var position = text.Lines[line - 1].Start + column - 1;

        var methodSymbol = await GetMethodSymbolAtPositionAsync(model, position, ct);
        if (methodSymbol is null) return new CalleesResult { SourceMethod = "No method found at position" };

        var callees = new List<CalleeInfo>();
        var dbOps = new List<DatabaseOperation>();
        var externalCalls = new List<ExternalCall>();
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        var methodDecl = await GetMethodDeclarationAsync(methodSymbol, ct);
        if (methodDecl is not null)
        {
            await TraverseAsync(methodDecl, model, document.Project.Solution, callees, dbOps, externalCalls, visited, maxDepth, 0, limit, ct);
        }

        var groupedExternal = externalCalls
            .GroupBy(e => e.Service)
            .Select(g => new ExternalCall
            {
                Service = g.Key,
                Type = g.First().Type,
                Operations = g.SelectMany(e => e.Operations).Distinct().ToList(),
                Locations = g.SelectMany(e => e.Locations).Distinct().ToList()
            })
            .ToList();

        return new CalleesResult
        {
            SourceMethod = $"{methodSymbol.ContainingType.Name}.{methodSymbol.Name}",
            Callees = callees.Take(limit).ToList(),
            DatabaseOperations = dbOps,
            ExternalCalls = groupedExternal,
            TotalCallees = callees.Count,
            MaxDepthReached = callees.Any(c => c.Depth >= maxDepth),
            AnalyzedAt = DateTime.UtcNow
        };
    }

    private async Task TraverseAsync(
        SyntaxNode node, SemanticModel model, Solution solution,
        List<CalleeInfo> callees, List<DatabaseOperation> dbOps, List<ExternalCall> externalCalls,
        HashSet<ISymbol> visited,
        int maxDepth, int currentDepth, int limit, CancellationToken ct)
    {
        if (currentDepth >= maxDepth || callees.Count >= limit) return;

        foreach (var inv in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (callees.Count >= limit) break;

            var invokedSymbol = model.GetSymbolInfo(inv).Symbol as IMethodSymbol;
            if (invokedSymbol is null) continue;

            var classification = Classify(invokedSymbol, inv, model);
            if (classification.Kind == CallKind.IntermediateLinq) continue;

            var span = inv.GetLocation().GetLineSpan();
            callees.Add(new CalleeInfo
            {
                Method = $"{invokedSymbol.ContainingType.Name}.{invokedSymbol.Name}",
                File = model.SyntaxTree.FilePath ?? "",
                Line = span.StartLinePosition.Line + 1,
                Column = span.StartLinePosition.Character + 1,
                CallType = classification.Kind switch
                {
                    CallKind.MediatR => "MediatR",
                    CallKind.Database => "Database",
                    _ => "Method"
                },
                TargetHandler = classification.HandlerName,
                Operation = classification.Operation,
                Entity = classification.Table,
                Depth = currentDepth
            });

            if (classification.Kind == CallKind.Database)
            {
                dbOps.Add(new DatabaseOperation
                {
                    Operation = classification.Operation ?? "QUERY",
                    Table = classification.Table ?? "",
                    Type = classification.IsWrite ? "Write" : "Read",
                    Location = $"line {span.StartLinePosition.Line + 1}",
                    Method = invokedSymbol.Name
                });
                // Don't recurse into EF method bodies.
                continue;
            }

            if (classification.Kind == CallKind.MediatR)
            {
                externalCalls.Add(new ExternalCall
                {
                    Service = "MediatR",
                    Type = "MediatR",
                    Operations = [classification.HandlerName ?? "Send"],
                    Locations = [$"line {span.StartLinePosition.Line + 1}"]
                });

                if (classification.HandlerMapping is not null)
                {
                    await FollowHandlerAsync(classification.HandlerMapping, solution,
                        callees, dbOps, externalCalls, visited,
                        maxDepth, currentDepth + 1, limit, ct);
                }
                continue;
            }

            // Interface or abstract dispatch: fan out to every implementation/override
            // so decorator chains (and any other indirection) are followed all the way down.
            if (invokedSymbol.IsAbstract || invokedSymbol.ContainingType.TypeKind == TypeKind.Interface)
            {
                if (!visited.Add(invokedSymbol)) continue;
                var impls = await SymbolFinder.FindImplementationsAsync(invokedSymbol, solution, cancellationToken: ct);
                foreach (var impl in impls.OfType<IMethodSymbol>())
                {
                    if (callees.Count >= limit) break;
                    if (!visited.Add(impl)) continue;
                    var implDecl = await GetMethodDeclarationAsync(impl, ct);
                    if (implDecl is null) continue;
                    var implModel = await GetModelForTreeAsync(implDecl.SyntaxTree, ct);
                    if (implModel is null) continue;
                    await TraverseAsync(implDecl, implModel, solution, callees, dbOps, externalCalls, visited,
                        maxDepth, currentDepth + 1, limit, ct);
                }
                continue;
            }

            // Plain method: recurse into body if in source and unvisited.
            if (!visited.Add(invokedSymbol)) continue;
            if (!invokedSymbol.DeclaringSyntaxReferences.Any()) continue;

            var calleeDecl = await GetMethodDeclarationAsync(invokedSymbol, ct);
            if (calleeDecl is null) continue;

            var calleeModel = await GetModelForTreeAsync(calleeDecl.SyntaxTree, ct);
            if (calleeModel is null) continue;

            await TraverseAsync(calleeDecl, calleeModel, solution, callees, dbOps, externalCalls, visited,
                maxDepth, currentDepth + 1, limit, ct);
        }
    }

    private async Task FollowHandlerAsync(
        HandlerMapping mapping, Solution solution,
        List<CalleeInfo> callees, List<DatabaseOperation> dbOps, List<ExternalCall> externalCalls,
        HashSet<ISymbol> visited,
        int maxDepth, int currentDepth, int limit, CancellationToken ct)
    {
        if (currentDepth >= maxDepth || callees.Count >= limit) return;
        if (string.IsNullOrEmpty(mapping.HandlerFilePath)) return;

        var doc = await _workspaceService.GetDocumentAsync(mapping.HandlerFilePath, ct);
        if (doc is null) return;

        var model = await doc.GetSemanticModelAsync(ct);
        if (model is null) return;

        var root = await model.SyntaxTree.GetRootAsync(ct);
        var handleMethod = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m =>
                m.Identifier.ValueText == "Handle" &&
                model.GetDeclaredSymbol(m)?.ContainingType.ToDisplayString() == mapping.HandlerFullName);

        if (handleMethod is null) return;

        var handleSymbol = model.GetDeclaredSymbol(handleMethod);
        if (handleSymbol is not null && !visited.Add(handleSymbol)) return;

        await TraverseAsync(handleMethod, model, solution, callees, dbOps, externalCalls, visited,
            maxDepth, currentDepth, limit, ct);
    }

    // --------------------------------------------------------------------
    // Classification — symbol-based, no name strings
    // --------------------------------------------------------------------

    private enum CallKind { Method, Database, MediatR, IntermediateLinq }

    private readonly record struct Classification(
        CallKind Kind,
        string? HandlerName,
        HandlerMapping? HandlerMapping,
        string? Operation,
        string? Table,
        bool IsWrite);

    private Classification Classify(IMethodSymbol method, InvocationExpressionSyntax inv, SemanticModel model)
    {
        var compilation = model.Compilation;
        var mediatR = GetMediatR(compilation);
        var efCore = GetEfCore(compilation);

        if (mediatR is not null && IsMediatRSend(method, mediatR))
        {
            var (handlerName, mapping) = ResolveMediatRSend(inv, model);
            return new Classification(CallKind.MediatR, handlerName, mapping, "Send", null, false);
        }

        if (efCore is not null)
        {
            var ef = ClassifyEfCall(method, inv, model, efCore);
            if (ef.HasValue) return ef.Value;
        }

        return new Classification(CallKind.Method, null, null, null, null, false);
    }

    private static bool IsMediatRSend(IMethodSymbol method, MediatRSymbols m)
    {
        if (method.Name != "Send") return false;
        var ct = method.ContainingType?.OriginalDefinition;
        return SymbolEqualityComparer.Default.Equals(ct, m.ISender) ||
               SymbolEqualityComparer.Default.Equals(ct, m.IMediator);
    }

    private (string? handlerName, HandlerMapping? mapping) ResolveMediatRSend(InvocationExpressionSyntax inv, SemanticModel model)
    {
        var arg0 = inv.ArgumentList.Arguments.FirstOrDefault();
        if (arg0 is null) return (null, null);

        var typeInfo = model.GetTypeInfo(arg0.Expression);
        var requestType = typeInfo.Type ?? typeInfo.ConvertedType;
        if (requestType is null) return (null, null);

        var mapping = _mediatRMappingService
            .FindHandlerForRequestSymbolAsync(requestType)
            .GetAwaiter().GetResult();

        return (mapping?.HandlerType, mapping);
    }

    private Classification? ClassifyEfCall(IMethodSymbol method, InvocationExpressionSyntax inv, SemanticModel model, EfCoreSymbols ef)
    {
        var name = method.Name;

        // SaveChanges[Async] on a DbContext (or derived).
        if (name is "SaveChanges" or "SaveChangesAsync")
        {
            var receiverType = GetReceiverType(inv, model);
            if (receiverType is not null && ef.DerivesFromDbContext(receiverType))
                return new Classification(CallKind.Database, null, null, "SAVE", null, true);
            return null;
        }

        // Methods defined on DbSet<T> (Add/Remove/Update/Attach/Find...).
        var containerOpen = method.ContainingType?.OriginalDefinition;
        if (SymbolEqualityComparer.Default.Equals(containerOpen, ef.DbSet))
        {
            return name switch
            {
                "Add" or "AddRange" or "AddAsync" or "AddRangeAsync"
                    => new Classification(CallKind.Database, null, null, "INSERT", DbSetTableName(inv, model, ef), true),
                "Update" or "UpdateRange"
                    => new Classification(CallKind.Database, null, null, "UPDATE", DbSetTableName(inv, model, ef), true),
                "Remove" or "RemoveRange"
                    => new Classification(CallKind.Database, null, null, "DELETE", DbSetTableName(inv, model, ef), true),
                "Attach" or "AttachRange"
                    => new Classification(CallKind.Database, null, null, "ATTACH", DbSetTableName(inv, model, ef), true),
                "Find" or "FindAsync"
                    => new Classification(CallKind.Database, null, null, "SELECT", DbSetTableName(inv, model, ef), false),
                _ => null
            };
        }

        // Extension methods on IQueryable / IEnumerable / EF helpers.
        if (method.IsExtensionMethod)
        {
            // Classify based on the static type of the actual receiver expression — not the
            // method's parameter type. `IQueryable<T>.ToList()` resolves to `Enumerable.ToList`
            // (whose param is IEnumerable<T>) but the call still hits the database.
            var receiverExpr = (inv.Expression as MemberAccessExpressionSyntax)?.Expression;
            var receiverType = receiverExpr is not null ? model.GetTypeInfo(receiverExpr).Type : null;

            var queryableInterface = model.Compilation.GetTypeByMetadataName("System.Linq.IQueryable`1");
            var enumerableInterface = model.Compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1");

            var receiverIsQueryable = receiverType is not null && queryableInterface is not null
                && AllInterfacesIncludingSelf(receiverType)
                    .Any(t => SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, queryableInterface));

            var receiverIsEnumerableOnly = !receiverIsQueryable
                && receiverType is not null && enumerableInterface is not null
                && AllInterfacesIncludingSelf(receiverType)
                    .Any(t => SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, enumerableInterface));

            if (receiverIsEnumerableOnly)
                return null; // in-memory LINQ — not a DB op, classify as Method

            if (receiverIsQueryable && receiverExpr is not null)
            {
                // Terminal iff return type is not IQueryable<T> (covers To*/First*/Count/Any/Find/etc).
                if (ReturnsQueryable(method.ReturnType, queryableInterface!))
                    return new Classification(CallKind.IntermediateLinq, null, null, null, null, false);

                return new Classification(CallKind.Database, null, null, "SELECT",
                    WalkToDbSet(receiverExpr, model, ef), false);
            }
        }

        return null;
    }

    private static IEnumerable<ITypeSymbol> AllInterfacesIncludingSelf(ITypeSymbol type)
    {
        yield return type;
        if (type is INamedTypeSymbol named)
            foreach (var i in named.AllInterfaces) yield return i;
    }

    // Returns true if the method's return type is IQueryable<T> (or wrapped: Task<IQueryable<T>> etc.).
    // Such a method is intermediate — it produces another queryable and doesn't execute.
    private static bool ReturnsQueryable(ITypeSymbol returnType, INamedTypeSymbol queryableInterface)
    {
        ITypeSymbol t = returnType;
        // Unwrap Task<...> / ValueTask<...>
        if (t is INamedTypeSymbol named && named.TypeArguments.Length == 1
            && (named.Name == "Task" || named.Name == "ValueTask")
            && named.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks")
        {
            t = named.TypeArguments[0];
        }
        return AllInterfacesIncludingSelf(t)
            .Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, queryableInterface));
    }

    private static ITypeSymbol? GetReceiverType(InvocationExpressionSyntax inv, SemanticModel model)
    {
        if (inv.Expression is MemberAccessExpressionSyntax m)
            return model.GetTypeInfo(m.Expression).Type;
        return null;
    }

    private static string? DbSetTableName(InvocationExpressionSyntax inv, SemanticModel model, EfCoreSymbols ef)
    {
        if (inv.Expression is not MemberAccessExpressionSyntax m) return null;
        return WalkToDbSet(m.Expression, model, ef);
    }

    private static string? WalkToDbSet(ExpressionSyntax expr, SemanticModel model, EfCoreSymbols ef)
    {
        var original = expr;
        while (true)
        {
            var t = model.GetTypeInfo(expr).Type?.OriginalDefinition;
            if (SymbolEqualityComparer.Default.Equals(t, ef.DbSet))
            {
                return expr switch
                {
                    MemberAccessExpressionSyntax mm => mm.Name.Identifier.ValueText,
                    IdentifierNameSyntax id => id.Identifier.ValueText,
                    InvocationExpressionSyntax setInv when model.GetTypeInfo(setInv).Type is INamedTypeSymbol nt && nt.TypeArguments.Length > 0
                        => nt.TypeArguments[0].Name,
                    _ => EntityNameFromQueryable(original, model)
                };
            }

            switch (expr)
            {
                case InvocationExpressionSyntax inner when inner.Expression is MemberAccessExpressionSyntax innerM:
                    expr = innerM.Expression;
                    break;
                case MemberAccessExpressionSyntax outerM:
                    expr = outerM.Expression;
                    break;
                default:
                    // Receiver chain dead-ends (local var, parameter, method result).
                    // Fall back to the entity type from the original receiver's IQueryable<T>.
                    return EntityNameFromQueryable(original, model);
            }
        }
    }

    private static string? EntityNameFromQueryable(ExpressionSyntax expr, SemanticModel model)
    {
        if (model.GetTypeInfo(expr).Type is INamedTypeSymbol n && n.TypeArguments.Length > 0)
            return n.TypeArguments[0].Name;
        return null;
    }

    private MediatRSymbols? GetMediatR(Compilation c)
    {
        if (!_mediatRCache.TryGetValue(c, out var s))
            _mediatRCache[c] = s = MediatRSymbols.From(c);
        return s;
    }

    private EfCoreSymbols? GetEfCore(Compilation c)
    {
        if (!_efCoreCache.TryGetValue(c, out var s))
            _efCoreCache[c] = s = EfCoreSymbols.From(c);
        return s;
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

    private static async Task<MethodDeclarationSyntax?> GetMethodDeclarationAsync(IMethodSymbol method, CancellationToken ct)
    {
        var first = method.DeclaringSyntaxReferences.FirstOrDefault();
        if (first is null) return null;
        var node = await first.GetSyntaxAsync(ct);
        return node as MethodDeclarationSyntax;
    }

    private async Task<SemanticModel?> GetModelForTreeAsync(SyntaxTree tree, CancellationToken ct)
    {
        var path = tree.FilePath;
        if (string.IsNullOrEmpty(path)) return null;
        var doc = await _workspaceService.GetDocumentAsync(path, ct);
        return doc is null ? null : await doc.GetSemanticModelAsync(ct);
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
