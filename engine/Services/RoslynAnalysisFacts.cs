using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Engine.Core.Analysis;

namespace Engine.Services;

/// <summary>
/// Shell adapter: binds the pure core to a Roslyn Solution. Every question the
/// core asks is answered by a targeted symbol/semantic-model query and cached
/// per id, so a single traversal over a huge codebase only pays for the methods
/// it actually walks.
/// </summary>
public sealed class RoslynAnalysisFacts : IAnalysisFacts
{
    private readonly Solution _solution;
    private readonly Compilation _rootCompilation;
    private readonly IMediatRMappingService _mediatR;
    private readonly CancellationToken _ct;

    private readonly Dictionary<string, IMethodSymbol> _methodsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, INamedTypeSymbol> _typesById = new(StringComparer.Ordinal);
    private readonly Dictionary<ProjectId, Compilation> _compByProject = new();

    private readonly Dictionary<MethodId, List<InvocationDescriptor>> _invCache = new();
    private readonly Dictionary<MethodId, List<MethodId>> _implsCache = new();
    private readonly Dictionary<TypeId, HandlerDescriptor?> _handlerCache = new();
    private readonly Dictionary<MethodId, MethodDescriptor> _methodDescCache = new();

    private Dictionary<TypeId, DbSetDescriptor>? _dbSetMap;

    public WellKnownTypes WellKnown { get; }

    public RoslynAnalysisFacts(Solution solution, Compilation rootCompilation,
        IMediatRMappingService mediatR, CancellationToken ct)
    {
        _solution = solution;
        _rootCompilation = rootCompilation;
        _compByProject[GetProjectIdForCompilation(rootCompilation)] = rootCompilation;
        _mediatR = mediatR;
        _ct = ct;
        WellKnown = BuildWellKnown(rootCompilation);
    }

    /// <summary>Register an entry-point method so its id can be resolved by the core.</summary>
    public MethodId Register(IMethodSymbol method)
    {
        RegisterMethodInternal(method);
        return IdOfMethod(method);
    }

    // --- IAnalysisFacts -----------------------------------------------------

    public IEnumerable<InvocationDescriptor> InvocationsIn(MethodId method)
    {
        if (_invCache.TryGetValue(method, out var list)) return list;
        list = ComputeInvocations(method);
        _invCache[method] = list;
        return list;
    }

    public IEnumerable<MethodId> ImplementationsOf(MethodId m)
    {
        if (_implsCache.TryGetValue(m, out var list)) return list;
        list = new List<MethodId>();
        if (_methodsById.TryGetValue(m.Value, out var target))
        {
            var impls = SymbolFinder.FindImplementationsAsync(target, _solution, cancellationToken: _ct)
                .GetAwaiter().GetResult();
            foreach (var impl in impls.OfType<IMethodSymbol>())
            {
                RegisterMethodInternal(impl);
                list.Add(IdOfMethod(impl));
            }
        }
        _implsCache[m] = list;
        return list;
    }

    public HandlerDescriptor? HandlerForRequest(TypeId requestType)
    {
        if (_handlerCache.TryGetValue(requestType, out var cached)) return cached;
        HandlerDescriptor? result = null;

        if (_typesById.TryGetValue(requestType.Value, out var reqSym))
        {
            var mapping = _mediatR.FindHandlerForRequestSymbolAsync(reqSym, _ct)
                .GetAwaiter().GetResult();
            if (mapping is not null)
            {
                var handlerSym = FindTypeByFullName(mapping.HandlerFullName);
                var handleSym = handlerSym?.GetMembers("Handle").OfType<IMethodSymbol>().FirstOrDefault();
                if (handlerSym is not null && handleSym is not null)
                {
                    RegisterMethodInternal(handleSym);
                    RegisterTypeInternal(handlerSym);
                    result = new HandlerDescriptor(
                        RequestType: requestType,
                        RequestTypeName: mapping.RequestType,
                        RequestFullName: mapping.RequestFullName,
                        HandleMethod: IdOfMethod(handleSym),
                        HandlerType: new TypeId(IdOfType(handlerSym)),
                        HandlerTypeName: mapping.HandlerType,
                        HandlerFullName: mapping.HandlerFullName,
                        ResponseTypeName: mapping.ResponseType,
                        IsCommand: mapping.IsCommand,
                        Definition: new SourceLocation(
                            mapping.HandlerFilePath ?? "", mapping.HandlerLine, mapping.HandlerColumn));
                }
            }
        }

        _handlerCache[requestType] = result;
        return result;
    }

    public DbSetDescriptor? DbSetForEntity(TypeId entityType)
    {
        _dbSetMap ??= BuildDbSetMap();
        return _dbSetMap.TryGetValue(entityType, out var d) ? d : null;
    }

    public bool InheritsFrom(TypeId type, TypeId baseType)
    {
        if (type == baseType) return true;
        if (!_typesById.TryGetValue(type.Value, out var t)) return false;
        for (var b = t.BaseType; b is not null; b = b.BaseType)
            if (IdOfType(b.OriginalDefinition) == baseType.Value) return true;
        return false;
    }

    public bool Implements(TypeId type, TypeId interfaceType)
    {
        if (type == interfaceType) return true;
        if (!_typesById.TryGetValue(type.Value, out var t)) return false;
        foreach (var i in t.AllInterfaces)
            if (IdOfType(i.OriginalDefinition) == interfaceType.Value) return true;
        return false;
    }

    public MethodDescriptor? GetMethod(MethodId id)
    {
        if (_methodDescCache.TryGetValue(id, out var d)) return d;
        if (!_methodsById.TryGetValue(id.Value, out var m)) return null;
        var desc = MakeMethodDescriptor(m);
        _methodDescCache[id] = desc;
        return desc;
    }

    // --- Invocation walking -------------------------------------------------

    private List<InvocationDescriptor> ComputeInvocations(MethodId methodId)
    {
        var result = new List<InvocationDescriptor>();
        if (!_methodsById.TryGetValue(methodId.Value, out var method)) return result;
        if (method.DeclaringSyntaxReferences.Length == 0) return result;

        foreach (var sref in method.DeclaringSyntaxReferences)
        {
            var node = sref.GetSyntax(_ct);
            var comp = GetCompilationFor(sref.SyntaxTree);
            if (comp is null) continue;
            SemanticModel model;
            try { model = comp.GetSemanticModel(sref.SyntaxTree); }
            catch { continue; }

            foreach (var inv in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(inv).Symbol is not IMethodSymbol target) continue;

                RegisterMethodInternal(target);

                var receiverType = GetReceiverType(inv, model);
                if (receiverType is INamedTypeSymbol rNamed) RegisterTypeInternal(rNamed);

                var argTypes = ImmutableArray.CreateBuilder<TypeRef>();
                foreach (var arg in inv.ArgumentList.Arguments)
                {
                    var ti = model.GetTypeInfo(arg.Expression);
                    var tSym = ti.Type ?? ti.ConvertedType;
                    if (tSym is null) continue;
                    if (tSym is INamedTypeSymbol nn) RegisterTypeInternal(nn);
                    argTypes.Add(MakeTypeRef(tSym));
                }

                var ls = inv.GetLocation().GetLineSpan();
                var line = ls.StartLinePosition.Line + 1;
                var col  = ls.StartLinePosition.Character + 1;
                var path = sref.SyntaxTree.FilePath ?? "";

                var site = new CallSiteId($"{IdOfMethod(target)}@{path}:{line}:{col}");
                result.Add(new InvocationDescriptor(
                    Site: site,
                    Target: MakeMethodDescriptor(target),
                    ReceiverType: receiverType is null ? null : MakeTypeRef(receiverType),
                    ArgumentTypes: argTypes.ToImmutable(),
                    Location: new SourceLocation(path, line, col)));
            }
        }
        return result;
    }

    private static ITypeSymbol? GetReceiverType(InvocationExpressionSyntax inv, SemanticModel model)
    {
        if (inv.Expression is MemberAccessExpressionSyntax m)
            return model.GetTypeInfo(m.Expression).Type;
        return null;
    }

    // --- Descriptor construction --------------------------------------------

    private MethodDescriptor MakeMethodDescriptor(IMethodSymbol m)
    {
        var def = m.OriginalDefinition;
        var id = IdOfMethod(def);
        if (_methodDescCache.TryGetValue(id, out var cached)) return cached;

        var containing = def.ContainingType is not null
            ? MakeTypeRef(def.ContainingType)
            : TypeRef.Unknown;
        var ret = MakeTypeRef(def.ReturnType);

        var paramsBuilder = ImmutableArray.CreateBuilder<ParameterRef>();
        foreach (var p in def.Parameters)
            paramsBuilder.Add(new ParameterRef(p.Name, MakeTypeRef(p.Type)));

        var loc = def.Locations.FirstOrDefault(l => l.IsInSource);
        SourceLocation? source = null;
        if (loc is not null)
        {
            var ls = loc.GetLineSpan();
            source = new SourceLocation(ls.Path, ls.StartLinePosition.Line + 1, ls.StartLinePosition.Character + 1);
        }

        var desc = new MethodDescriptor(
            Id: id,
            Name: def.Name,
            ContainingType: containing,
            IsAbstract: def.IsAbstract,
            IsStatic: def.IsStatic,
            IsExtensionMethod: def.IsExtensionMethod,
            ReturnType: ret,
            Parameters: paramsBuilder.ToImmutable(),
            OriginalDefinitionId: null,
            Definition: source);
        _methodDescCache[id] = desc;
        return desc;
    }

    private TypeRef MakeTypeRef(ITypeSymbol t)
    {
        var def = t.OriginalDefinition;
        var id = IdOfType(def);
        var name = t.Name;
        var full = t.ToDisplayString();
        var isIface = def.TypeKind == TypeKind.Interface;

        var typeArgs = ImmutableArray<TypeRef>.Empty;
        if (t is INamedTypeSymbol n && n.TypeArguments.Length > 0)
        {
            var b = ImmutableArray.CreateBuilder<TypeRef>(n.TypeArguments.Length);
            foreach (var ta in n.TypeArguments) b.Add(MakeTypeRef(ta));
            typeArgs = b.MoveToImmutable();
        }
        return new TypeRef(new TypeId(id), name, full, isIface, typeArgs);
    }

    // --- Id stringification -------------------------------------------------

    private static string IdOfType(ITypeSymbol t)
        => t.OriginalDefinition.GetDocumentationCommentId() ?? t.OriginalDefinition.ToDisplayString();

    private static MethodId IdOfMethod(IMethodSymbol m)
        => new(m.OriginalDefinition.GetDocumentationCommentId() ?? m.OriginalDefinition.ToDisplayString());

    private static TypeId IdOfType_Type(ITypeSymbol t) => new(IdOfType(t));

    // Non-static so it can register too.
    private void RegisterMethodInternal(IMethodSymbol m)
    {
        var def = m.OriginalDefinition;
        var id = IdOfMethod(def).Value;
        if (!_methodsById.ContainsKey(id)) _methodsById[id] = def;
        if (def.ContainingType is INamedTypeSymbol c) RegisterTypeInternal(c);
    }

    private void RegisterTypeInternal(INamedTypeSymbol t)
    {
        var def = t.OriginalDefinition;
        var id = IdOfType(def);
        if (!_typesById.ContainsKey(id)) _typesById[id] = def;
        foreach (var ta in t.TypeArguments.OfType<INamedTypeSymbol>())
            RegisterTypeInternal(ta);
    }

    // --- WellKnownTypes -----------------------------------------------------

    private static WellKnownTypes BuildWellKnown(Compilation c) => new(
        IRequestHandler2: TryTypeId(c, "MediatR.IRequestHandler`2"),
        IRequestHandler1: TryTypeId(c, "MediatR.IRequestHandler`1"),
        ISender:          TryTypeId(c, "MediatR.ISender"),
        IMediator:        TryTypeId(c, "MediatR.IMediator"),
        DbContext:        TryTypeId(c, "Microsoft.EntityFrameworkCore.DbContext"),
        DbSet:            TryTypeId(c, "Microsoft.EntityFrameworkCore.DbSet`1"),
        IQueryable:       TryTypeId(c, "System.Linq.IQueryable`1"),
        IEnumerable:      TryTypeId(c, "System.Collections.Generic.IEnumerable`1"),
        Task:             TryTypeId(c, "System.Threading.Tasks.Task`1"),
        ValueTask:        TryTypeId(c, "System.Threading.Tasks.ValueTask`1"));

    private static TypeId? TryTypeId(Compilation c, string metadataName)
    {
        var s = c.GetTypeByMetadataName(metadataName);
        return s is null ? null : new TypeId(IdOfType(s));
    }

    // --- DbSet map ----------------------------------------------------------

    private Dictionary<TypeId, DbSetDescriptor> BuildDbSetMap()
    {
        var map = new Dictionary<TypeId, DbSetDescriptor>();
        if (WellKnown.DbContext is not { } dbCtxId || WellKnown.DbSet is not { } dbSetId)
            return map;

        foreach (var proj in _solution.Projects)
        {
            var comp = GetCompilationForProject(proj);
            if (comp is null) continue;
            foreach (var type in AllNamedTypes(comp.GlobalNamespace))
            {
                if (type.TypeKind != TypeKind.Class) continue;
                if (!DerivesFromById(type, dbCtxId)) continue;
                RegisterTypeInternal(type);
                var ctxId = new TypeId(IdOfType(type));
                foreach (var prop in type.GetMembers().OfType<IPropertySymbol>())
                {
                    if (prop.Type is not INamedTypeSymbol named) continue;
                    if (IdOfType(named.OriginalDefinition) != dbSetId.Value) continue;
                    if (named.TypeArguments.Length == 0) continue;
                    if (named.TypeArguments[0] is not INamedTypeSymbol entity) continue;
                    RegisterTypeInternal(entity);
                    var entityId = new TypeId(IdOfType(entity));
                    map[entityId] = new DbSetDescriptor(prop.Name, entityId, ctxId);
                }
            }
        }
        return map;
    }

    private static bool DerivesFromById(INamedTypeSymbol t, TypeId baseId)
    {
        for (var b = t.BaseType; b is not null; b = b.BaseType)
            if (IdOfType(b.OriginalDefinition) == baseId.Value) return true;
        return false;
    }

    private static IEnumerable<INamedTypeSymbol> AllNamedTypes(INamespaceSymbol ns)
    {
        foreach (var t in ns.GetTypeMembers())
        {
            yield return t;
            foreach (var nested in NestedTypes(t)) yield return nested;
        }
        foreach (var child in ns.GetNamespaceMembers())
            foreach (var t in AllNamedTypes(child)) yield return t;
    }

    private static IEnumerable<INamedTypeSymbol> NestedTypes(INamedTypeSymbol t)
    {
        foreach (var n in t.GetTypeMembers())
        {
            yield return n;
            foreach (var nn in NestedTypes(n)) yield return nn;
        }
    }

    // --- Compilation lookup -------------------------------------------------

    private Compilation? GetCompilationFor(SyntaxTree tree)
    {
        var doc = _solution.GetDocument(tree);
        if (doc is null) return _rootCompilation;
        return GetCompilationForProject(doc.Project);
    }

    private Compilation? GetCompilationForProject(Project proj)
    {
        if (_compByProject.TryGetValue(proj.Id, out var c)) return c;
        c = proj.GetCompilationAsync(_ct).GetAwaiter().GetResult();
        if (c is not null) _compByProject[proj.Id] = c;
        return c;
    }

    private static ProjectId GetProjectIdForCompilation(Compilation c)
    {
        // Compilation doesn't expose project id directly; use assembly as a proxy handle.
        // Safe because we only use this dictionary to avoid re-fetching the root comp.
        return ProjectId.CreateNewId(c.AssemblyName);
    }

    // --- Type lookup by full name (cross-project) --------------------------

    private INamedTypeSymbol? FindTypeByFullName(string fullName)
    {
        foreach (var comp in _compByProject.Values)
        {
            var t = comp.GetTypeByMetadataName(fullName);
            if (t is not null) return t;
        }
        foreach (var proj in _solution.Projects)
        {
            var comp = GetCompilationForProject(proj);
            var t = comp?.GetTypeByMetadataName(fullName);
            if (t is not null) return t;
        }
        return null;
    }
}
