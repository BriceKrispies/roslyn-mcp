namespace Engine.Core.Analysis;

/// <summary>
/// The port the core calls into for every question that requires touching
/// the compilation. Shell-side implementations (e.g. RoslynAnalysisFacts)
/// must answer queries lazily and cache — <see cref="InvocationsIn"/> and
/// <see cref="ImplementationsOf"/> are expected to be called repeatedly on
/// the same ids during graph traversal on large codebases.
///
/// Design contract:
/// - All enumerables are lazy. The core iterates with early-exit semantics
///   (depth/limit), and iterator cost is amortized across the traversal.
/// - Shell implementations MUST memoize per-id results; the core does not
///   re-query unnecessarily but will revisit ids across traversals.
/// - No Microsoft.CodeAnalysis.* types leak through this interface.
/// </summary>
public interface IAnalysisFacts
{
    WellKnownTypes WellKnown { get; }

    /// <summary>
    /// Enumerate all invocations inside the body of a method, in source order.
    /// Returns empty for abstract/interface methods or methods with no source.
    /// MUST be lazy — for large methods, callers early-exit when depth/limit hit.
    /// </summary>
    IEnumerable<InvocationDescriptor> InvocationsIn(MethodId method);

    /// <summary>
    /// All concrete overrides/implementations of an interface or abstract
    /// method, across the whole solution. Expensive; shell MUST cache.
    /// </summary>
    IEnumerable<MethodId> ImplementationsOf(MethodId abstractOrInterfaceMethod);

    /// <summary>Resolve a MediatR request type to the matching handler.</summary>
    HandlerDescriptor? HandlerForRequest(TypeId requestType);

    /// <summary>
    /// For a given entity type, return the DbSet&lt;T&gt; property descriptor
    /// on whichever DbContext declares it. Returns null if no DbContext in
    /// the compilation exposes a DbSet of that entity.
    /// </summary>
    DbSetDescriptor? DbSetForEntity(TypeId entityType);

    /// <summary>Does <paramref name="type"/> derive from <paramref name="baseType"/> (transitive)?</summary>
    bool InheritsFrom(TypeId type, TypeId baseType);

    /// <summary>
    /// Does <paramref name="type"/> (or any base/interface) implement
    /// <paramref name="interfaceType"/> (compared by open-generic definition)?
    /// </summary>
    bool Implements(TypeId type, TypeId interfaceType);

    /// <summary>Retrieve method metadata by id. Null if unknown.</summary>
    MethodDescriptor? GetMethod(MethodId id);
}
