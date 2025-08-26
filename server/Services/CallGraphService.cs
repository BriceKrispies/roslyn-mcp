using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using mcp_server.Models;
using System.Collections.Concurrent;

namespace mcp_server.Services;

public class CallGraphService : ICallGraphService
{
    private readonly IRoslynWorkspaceService _workspaceService;
    private readonly ICacheService _cacheService;
    private readonly IMediatRMappingService _mediatRMappingService;
    private readonly ILogger<CallGraphService> _logger;

    public CallGraphService(
        IRoslynWorkspaceService workspaceService,
        ICacheService cacheService,
        IMediatRMappingService mediatRMappingService,
        ILogger<CallGraphService> logger)
    {
        _workspaceService = workspaceService;
        _cacheService = cacheService;
        _mediatRMappingService = mediatRMappingService;
        _logger = logger;
    }

    public async Task<CallersResult> FindCallersFromLocationAsync(string filePath, int line, int column, int maxDepth = 5, int limit = 100, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Finding callers from {FilePath}:{Line}:{Column}", filePath, line, column);

        var document = await _workspaceService.GetDocumentAsync(filePath, cancellationToken);
        if (document == null)
        {
            return new CallersResult { TargetMethod = "Document not found" };
        }

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
        
        if (semanticModel == null || syntaxTree == null)
        {
            return new CallersResult { TargetMethod = "Unable to analyze document" };
        }

        var text = await document.GetTextAsync(cancellationToken);
        var position = text.Lines[line - 1].Start + column - 1;
        var root = await syntaxTree.GetRootAsync(cancellationToken);
        var node = root.FindToken(position).Parent;

        // Find the method containing this position
        var methodSymbol = await GetMethodSymbolAtPositionAsync(semanticModel, position, cancellationToken);
        if (methodSymbol == null)
        {
            return new CallersResult { TargetMethod = "No method found at position" };
        }

        var targetMethod = $"{methodSymbol.ContainingType.Name}.{methodSymbol.Name}";
        _logger.LogDebug("Target method: {TargetMethod}", targetMethod);

        var callers = new List<CallerInfo>();
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var solution = document.Project.Solution;

        await FindCallersRecursiveAsync(methodSymbol, solution, callers, visited, maxDepth, 0, limit, cancellationToken);

        return new CallersResult
        {
            TargetMethod = targetMethod,
            Callers = callers.Take(limit),
            TotalCallers = callers.Count,
            MaxDepthReached = callers.Any(c => c.Depth >= maxDepth),
            AnalyzedAt = DateTime.UtcNow
        };
    }

    public async Task<CalleesResult> FindCalleesFromLocationAsync(string filePath, int line, int column, int maxDepth = 5, int limit = 200, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Finding callees from {FilePath}:{Line}:{Column}", filePath, line, column);

        // Build MediatR mappings first
        await _mediatRMappingService.BuildMappingsAsync(cancellationToken);

        var document = await _workspaceService.GetDocumentAsync(filePath, cancellationToken);
        if (document == null)
        {
            return new CalleesResult { SourceMethod = "Document not found" };
        }

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
        
        if (semanticModel == null || syntaxTree == null)
        {
            return new CalleesResult { SourceMethod = "Unable to analyze document" };
        }

        var text = await document.GetTextAsync(cancellationToken);
        var position = text.Lines[line - 1].Start + column - 1;
        var root = await syntaxTree.GetRootAsync(cancellationToken);

        // Find the method containing this position
        var methodSymbol = await GetMethodSymbolAtPositionAsync(semanticModel, position, cancellationToken);
        if (methodSymbol == null)
        {
            return new CalleesResult { SourceMethod = "No method found at position" };
        }

        var sourceMethod = $"{methodSymbol.ContainingType.Name}.{methodSymbol.Name}";
        _logger.LogDebug("Source method: {SourceMethod}", sourceMethod);

        var callees = new List<CalleeInfo>();
        var databaseOps = new List<DatabaseOperation>();
        var externalCalls = new List<ExternalCall>();
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        // Find the method declaration
        var methodDeclaration = await GetMethodDeclarationAsync(methodSymbol, cancellationToken);
        if (methodDeclaration != null)
        {
            await FindCalleesRecursiveAsync(methodDeclaration, semanticModel, callees, databaseOps, externalCalls, visited, maxDepth, 0, limit, cancellationToken);
        }

        // Group external calls by service type
        var groupedExternalCalls = externalCalls
            .GroupBy(e => e.Service)
            .Select(g => new ExternalCall
            {
                Service = g.Key,
                Type = g.First().Type,
                Operations = g.SelectMany(e => e.Operations).Distinct(),
                Locations = g.SelectMany(e => e.Locations).Distinct()
            });

        return new CalleesResult
        {
            SourceMethod = sourceMethod,
            Callees = callees.Take(limit),
            DatabaseOperations = databaseOps,
            ExternalCalls = groupedExternalCalls,
            TotalCallees = callees.Count,
            MaxDepthReached = callees.Any(c => c.Depth >= maxDepth),
            AnalyzedAt = DateTime.UtcNow
        };
    }

    private async Task<IMethodSymbol?> GetMethodSymbolAtPositionAsync(SemanticModel semanticModel, int position, CancellationToken cancellationToken)
    {
        var root = await semanticModel.SyntaxTree.GetRootAsync(cancellationToken);
        var token = root.FindToken(position);
        var node = token.Parent;

        // Walk up the syntax tree to find a method declaration
        while (node != null)
        {
            if (node is MethodDeclarationSyntax methodDecl)
            {
                return semanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
            }
            if (node is LocalFunctionStatementSyntax localFunc)
            {
                return semanticModel.GetDeclaredSymbol(localFunc) as IMethodSymbol;
            }
            if (node is AccessorDeclarationSyntax accessor)
            {
                return semanticModel.GetDeclaredSymbol(accessor) as IMethodSymbol;
            }
            node = node.Parent;
        }

        return null;
    }

    private async Task<MethodDeclarationSyntax?> GetMethodDeclarationAsync(IMethodSymbol methodSymbol, CancellationToken cancellationToken)
    {
        var declaringSyntax = await methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntaxAsync(cancellationToken);
        return declaringSyntax as MethodDeclarationSyntax;
    }

    private async Task FindCallersRecursiveAsync(ISymbol targetSymbol, Solution solution, List<CallerInfo> callers, HashSet<ISymbol> visited, int maxDepth, int currentDepth, int limit, CancellationToken cancellationToken)
    {
        if (currentDepth >= maxDepth || callers.Count >= limit || visited.Contains(targetSymbol))
        {
            return;
        }

        visited.Add(targetSymbol);

        try
        {
            var references = await SymbolFinder.FindReferencesAsync(targetSymbol, solution, cancellationToken);
            
            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    if (callers.Count >= limit) return;

                    var document = solution.GetDocument(location.Document.Id);
                    if (document == null) continue;

                    var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                    if (semanticModel == null) continue;

                    var root = await document.GetSyntaxRootAsync(cancellationToken);
                    if (root == null) continue;

                    var node = root.FindNode(location.Location.SourceSpan);
                    var callingMethod = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                    
                    if (callingMethod == null) continue;

                    var callingSymbol = semanticModel.GetDeclaredSymbol(callingMethod);
                    if (callingSymbol == null) continue;

                    var lineSpan = location.Location.GetLineSpan();
                    var callerInfo = new CallerInfo
                    {
                        Method = $"{callingSymbol.ContainingType.Name}.{callingSymbol.Name}",
                        File = document.FilePath ?? "",
                        Line = lineSpan.StartLinePosition.Line + 1,
                        Column = lineSpan.StartLinePosition.Character + 1,
                        Depth = currentDepth,
                        CallChain = BuildCallChain(callingSymbol, targetSymbol),
                        EndpointInfo = await GetEndpointInfoAsync(callingSymbol, semanticModel, cancellationToken)
                    };

                    callers.Add(callerInfo);

                    // Recursively find callers of this calling method
                    await FindCallersRecursiveAsync(callingSymbol, solution, callers, visited, maxDepth, currentDepth + 1, limit, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error finding callers for symbol: {Symbol}", targetSymbol.Name);
        }
    }

    private async Task FindCalleesRecursiveAsync(SyntaxNode node, SemanticModel semanticModel, List<CalleeInfo> callees, List<DatabaseOperation> databaseOps, List<ExternalCall> externalCalls, HashSet<ISymbol> visited, int maxDepth, int currentDepth, int limit, CancellationToken cancellationToken)
    {
        if (currentDepth >= maxDepth || callees.Count >= limit)
        {
            return;
        }

        // Find all invocation expressions in this node
        var invocations = node.DescendantNodes().OfType<InvocationExpressionSyntax>();
        
        foreach (var invocation in invocations)
        {
            if (callees.Count >= limit) break;

            try
            {
                var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                var invokedSymbol = symbolInfo.Symbol as IMethodSymbol;
                
                if (invokedSymbol == null) continue;
                
                // For MediatR calls, we want to track each call separately even if it's the same Send method
                // because they have different request types and target different handlers
                var isMediatRCall = (invokedSymbol.ContainingType.Name == "ISender" || invokedSymbol.ContainingType.Name == "IMediator") && invokedSymbol.Name == "Send";
                
                if (!isMediatRCall && visited.Contains(invokedSymbol)) continue;

                var lineSpan = invocation.GetLocation().GetLineSpan();
                var calleeInfo = new CalleeInfo
                {
                    Method = $"{invokedSymbol.ContainingType.Name}.{invokedSymbol.Name}",
                    File = semanticModel.SyntaxTree.FilePath ?? "",
                    Line = lineSpan.StartLinePosition.Line + 1,
                    Column = lineSpan.StartLinePosition.Character + 1,
                    Depth = currentDepth
                };

                // Classify the call type
                var callType = ClassifyCallType(invokedSymbol, invocation, semanticModel);
                calleeInfo.CallType = callType.Type;
                calleeInfo.TargetHandler = callType.TargetHandler;
                calleeInfo.Operation = callType.Operation;
                calleeInfo.Entity = callType.Entity;

                callees.Add(calleeInfo);

                // Handle special cases
                if (callType.Type == "Database")
                {
                    var dbOp = CreateDatabaseOperation(invokedSymbol, invocation, semanticModel);
                    if (dbOp != null)
                    {
                        databaseOps.Add(dbOp);
                    }
                }
                else if (callType.Type == "MediatR")
                {
                    var extCall = CreateExternalCall("MediatR", callType.TargetHandler ?? invokedSymbol.Name, invocation);
                    externalCalls.Add(extCall);
                }

                visited.Add(invokedSymbol);

                // Recursively analyze the called method if it's in our solution
                if (invokedSymbol.DeclaringSyntaxReferences.Any())
                {
                    var methodDecl = await GetMethodDeclarationAsync(invokedSymbol, cancellationToken);
                    if (methodDecl != null)
                    {
                        await FindCalleesRecursiveAsync(methodDecl, semanticModel, callees, databaseOps, externalCalls, visited, maxDepth, currentDepth + 1, limit, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error analyzing invocation: {Invocation}", invocation.ToString());
            }
        }
    }

    private (string Type, string? TargetHandler, string? Operation, string? Entity) ClassifyCallType(IMethodSymbol method, InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var typeName = method.ContainingType.Name;
        var methodName = method.Name;

        // MediatR patterns
        if ((typeName == "IMediator" || typeName == "ISender") && methodName == "Send")
        {
            var targetHandler = ExtractMediatRHandler(invocation, semanticModel);
            return ("MediatR", targetHandler, "Send", null);
        }

        // Entity Framework patterns
        if (IsEntityFrameworkMethod(method))
        {
            var entity = ExtractEntityName(invocation, semanticModel);
            var operation = ClassifyDatabaseOperation(methodName);
            return ("Database", null, operation, entity);
        }

        // Regular method call
        return ("Method", null, null, null);
    }

    private bool IsEntityFrameworkMethod(IMethodSymbol method)
    {
        var typeName = method.ContainingType.ToDisplayString();
        
        return typeName.Contains("Microsoft.EntityFrameworkCore") ||
               typeName.Contains("System.Linq") && method.Name.EndsWith("Async") ||
               method.Name is "ToListAsync" or "FirstOrDefaultAsync" or "SingleOrDefaultAsync" or "AnyAsync" or "CountAsync" or "Where" or "Select" or "OrderBy" or "OrderByDescending" or "Include" or "ThenInclude";
    }

    private string ClassifyDatabaseOperation(string methodName)
    {
        return methodName switch
        {
            "ToListAsync" or "FirstOrDefaultAsync" or "SingleOrDefaultAsync" or "AnyAsync" or "CountAsync" => "SELECT",
            "AddAsync" or "Add" => "INSERT",
            "Update" => "UPDATE",
            "Remove" => "DELETE",
            _ => "QUERY"
        };
    }

    private string? ExtractMediatRHandler(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        // Look for new SomeQuery() or new SomeCommand() in the arguments
        var objectCreation = invocation.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault();
        if (objectCreation != null)
        {
            var typeInfo = semanticModel.GetTypeInfo(objectCreation);
            if (typeInfo.Type != null)
            {
                var requestTypeName = typeInfo.Type.Name;
                
                // Try to find the handler using the mapping service
                try
                {
                    var mapping = _mediatRMappingService.FindHandlerForRequestAsync(requestTypeName).GetAwaiter().GetResult();
                    if (mapping != null)
                    {
                        return mapping.HandlerType;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to find handler for request: {RequestType}", requestTypeName);
                }
                
                // Fallback to request type name if handler not found
                return requestTypeName;
            }
        }

        return null;
    }

    private string? ExtractEntityName(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        // Try to extract entity name from _context.EntitySet patterns
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var expression = memberAccess.Expression;
            while (expression is MemberAccessExpressionSyntax nestedMember)
            {
                if (nestedMember.Name.Identifier.ValueText != "Set" && 
                    !nestedMember.Name.Identifier.ValueText.StartsWith("_"))
                {
                    return nestedMember.Name.Identifier.ValueText;
                }
                expression = nestedMember.Expression;
            }
            
            return memberAccess.Name.Identifier.ValueText;
        }

        return null;
    }

    private DatabaseOperation? CreateDatabaseOperation(IMethodSymbol method, InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var operation = ClassifyDatabaseOperation(method.Name);
        var entity = ExtractEntityName(invocation, semanticModel);
        var lineSpan = invocation.GetLocation().GetLineSpan();

        return new DatabaseOperation
        {
            Operation = operation,
            Table = entity ?? "Unknown",
            Type = operation is "SELECT" or "QUERY" ? "Read" : "Write",
            Location = $"line {lineSpan.StartLinePosition.Line + 1}",
            Method = method.Name
        };
    }

    private ExternalCall CreateExternalCall(string service, string operation, InvocationExpressionSyntax invocation)
    {
        var lineSpan = invocation.GetLocation().GetLineSpan();
        
        return new ExternalCall
        {
            Service = service,
            Type = service,
            Operations = [operation],
            Locations = [$"line {lineSpan.StartLinePosition.Line + 1}"]
        };
    }

    private async Task<EndpointInfo?> GetEndpointInfoAsync(ISymbol methodSymbol, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        if (methodSymbol is not IMethodSymbol method) return null;

        var containingType = method.ContainingType;
        
        // Check if it's a controller
        if (containingType.Name.EndsWith("Controller") && 
            containingType.BaseType?.Name == "Controller")
        {
            // Try to extract route information from attributes
            var httpMethod = ExtractHttpMethod(method);
            var route = ExtractRoute(method, containingType);

            return new EndpointInfo
            {
                IsController = true,
                ControllerName = containingType.Name.Replace("Controller", ""),
                ActionName = method.Name,
                HttpMethod = httpMethod,
                Route = route
            };
        }

        return null;
    }

    private string ExtractHttpMethod(IMethodSymbol method)
    {
        var attributes = method.GetAttributes();
        
        foreach (var attr in attributes)
        {
            var name = attr.AttributeClass?.Name;
            if (name?.EndsWith("Attribute") == true)
            {
                name = name[..^9]; // Remove "Attribute" suffix
            }

            return name switch
            {
                "HttpGet" or "Get" => "GET",
                "HttpPost" or "Post" => "POST",
                "HttpPut" or "Put" => "PUT",
                "HttpDelete" or "Delete" => "DELETE",
                "HttpPatch" or "Patch" => "PATCH",
                _ => "GET" // Default assumption
            };
        }

        return "GET";
    }

    private string ExtractRoute(IMethodSymbol method, INamedTypeSymbol controller)
    {
        // This is a simplified route extraction - in reality, you'd need more complex logic
        var controllerName = controller.Name.Replace("Controller", "").ToLowerInvariant();
        var actionName = method.Name.ToLowerInvariant();
        
        return $"/{controllerName}/{actionName}";
    }

    private IEnumerable<string> BuildCallChain(ISymbol caller, ISymbol target)
    {
        return [$"{caller.ContainingType.Name}.{caller.Name} → {target.ContainingType.Name}.{target.Name}"];
    }
}
