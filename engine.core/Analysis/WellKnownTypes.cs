namespace Engine.Core.Analysis;

/// <summary>
/// Well-known framework types the classifier cares about. Populated by the
/// shell per compilation. If a type isn't available in the current compilation
/// its TypeId is null — classifier must treat that as "framework absent" and
/// skip rules that depend on it.
/// </summary>
public sealed record WellKnownTypes(
    TypeId? IRequestHandler2,
    TypeId? IRequestHandler1,
    TypeId? ISender,
    TypeId? IMediator,
    TypeId? DbContext,
    TypeId? DbSet,
    TypeId? IQueryable,
    TypeId? IEnumerable,
    TypeId? Task,
    TypeId? ValueTask)
{
    public bool HasMediatR => IRequestHandler2 is not null || IRequestHandler1 is not null;
    public bool HasEfCore => DbContext is not null && DbSet is not null;

    public static WellKnownTypes Empty => new(
        null, null, null, null, null, null, null, null, null, null);
}
