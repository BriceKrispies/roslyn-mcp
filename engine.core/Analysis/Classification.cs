namespace Engine.Core.Analysis;

public enum CallKind
{
    /// <summary>Ordinary method invocation — recurse into the body.</summary>
    Method,

    /// <summary>EF Core database operation (Add, SaveChanges, ToListAsync, ...).</summary>
    Database,

    /// <summary>MediatR ISender/IMediator.Send — follow to the resolved handler.</summary>
    MediatR,

    /// <summary>
    /// Intermediate LINQ on IQueryable (Where/Select/OrderBy/...): returns IQueryable,
    /// so it's not a DB execution. Classifier signals the builder to skip it entirely.
    /// </summary>
    IntermediateLinq,
}

/// <summary>
/// The entity a DB op touches. Both the CLR entity type (always available,
/// stable across query rewrites) and — when the shell could resolve it —
/// the DbSet property name on the containing DbContext (useful as a
/// user-facing "table" label).
/// </summary>
public sealed record EntityRef(string EntityType, string? DbSetName)
{
    public string DisplayName => DbSetName ?? EntityType;
}

public sealed record Classification(
    CallKind Kind,
    string? Operation = null,
    EntityRef? Entity = null,
    HandlerDescriptor? Handler = null,
    bool IsWrite = false)
{
    public static Classification Method() => new(CallKind.Method);
    public static Classification Intermediate() => new(CallKind.IntermediateLinq);
}
