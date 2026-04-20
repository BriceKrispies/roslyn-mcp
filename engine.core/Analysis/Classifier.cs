namespace Engine.Core.Analysis;

/// <summary>
/// Pure classification of a single invocation. Takes enough context to answer
/// any question via <see cref="IAnalysisFacts"/> without ever touching a
/// compilation or syntax tree directly. Every rule encoded here is unit-testable.
/// </summary>
public static class Classifier
{
    public static Classification Classify(InvocationDescriptor inv, IAnalysisFacts facts)
    {
        var wk = facts.WellKnown;

        if (wk.HasMediatR && IsMediatRSend(inv, wk))
            return ClassifyMediatRSend(inv, facts);

        if (wk.HasEfCore)
        {
            var ef = ClassifyEfCall(inv, facts, wk);
            if (ef is not null) return ef;
        }

        return Classification.Method();
    }

    // --- MediatR ------------------------------------------------------------

    private static bool IsMediatRSend(InvocationDescriptor inv, WellKnownTypes wk)
    {
        if (inv.Target.Name != "Send") return false;
        var container = inv.Target.ContainingType.Id;
        return container == wk.ISender || container == wk.IMediator;
    }

    private static Classification ClassifyMediatRSend(InvocationDescriptor inv, IAnalysisFacts facts)
    {
        if (inv.ArgumentTypes.IsDefaultOrEmpty)
            return new Classification(CallKind.MediatR, "Send");
        var requestType = inv.ArgumentTypes[0];
        var handler = facts.HandlerForRequest(requestType.Id);
        return new Classification(CallKind.MediatR, "Send", Handler: handler);
    }

    // --- EF Core ------------------------------------------------------------

    private static Classification? ClassifyEfCall(InvocationDescriptor inv, IAnalysisFacts facts, WellKnownTypes wk)
    {
        var name = inv.Target.Name;

        // SaveChanges[Async] on a DbContext (or derived).
        if (name is "SaveChanges" or "SaveChangesAsync")
        {
            if (inv.ReceiverType is not null
                && wk.DbContext is { } dbCtxId
                && facts.InheritsFrom(inv.ReceiverType.Id, dbCtxId))
            {
                return new Classification(CallKind.Database, "SAVE", Entity: null, IsWrite: true);
            }
            return null;
        }

        // Methods defined directly on DbSet<T>.
        if (wk.DbSet is { } dbSetOpenId)
        {
            var containerOpen = inv.Target.ContainingType.Id;
            // ContainingType.Id for DbSet<User> is the closed-generic id.
            // We compare against the open-generic DbSet id recorded in WellKnown.
            // Shell should emit ContainingType.Id as the open-generic id for
            // consistency; see RoslynAnalysisFacts.MakeTypeRef.
            if (containerOpen == dbSetOpenId)
                return ClassifyDbSetMember(inv, name, facts);
        }

        // Extension methods on IQueryable (EF-executed) vs IEnumerable (in-memory LINQ).
        if (inv.Target.IsExtensionMethod && inv.ReceiverType is not null
            && wk.IQueryable is { } queryableId)
        {
            var receiverIsQueryable = facts.Implements(inv.ReceiverType.Id, queryableId);
            if (!receiverIsQueryable) return null; // plain in-memory LINQ — Method kind

            // Terminal iff return type doesn't itself expose IQueryable<T>.
            if (ReturnsQueryable(inv.Target.ReturnType, facts, wk))
                return Classification.Intermediate();

            var entity = EntityFromQueryable(inv.ReceiverType, facts);
            return new Classification(CallKind.Database, "SELECT", Entity: entity, IsWrite: false);
        }

        return null;
    }

    private static Classification ClassifyDbSetMember(InvocationDescriptor inv, string name, IAnalysisFacts facts)
    {
        var (op, isWrite) = name switch
        {
            "Add" or "AddRange" or "AddAsync" or "AddRangeAsync" => ("INSERT", true),
            "Update" or "UpdateRange"                            => ("UPDATE", true),
            "Remove" or "RemoveRange"                            => ("DELETE", true),
            "Attach" or "AttachRange"                            => ("ATTACH", true),
            "Find" or "FindAsync"                                => ("SELECT", false),
            _                                                     => (null, false),
        };
        if (op is null) return Classification.Method();

        var entity = EntityFromDbSet(inv.ReceiverType, facts);
        return new Classification(CallKind.Database, op, entity, IsWrite: isWrite);
    }

    // --- Entity / table naming ---------------------------------------------

    private static EntityRef? EntityFromDbSet(TypeRef? receiverType, IAnalysisFacts facts)
    {
        // Receiver of a DbSet method is `DbSet<T>` itself.
        if (receiverType is null || receiverType.TypeArguments.IsDefaultOrEmpty) return null;
        var entityType = receiverType.TypeArguments[0];
        var dbSet = facts.DbSetForEntity(entityType.Id);
        return new EntityRef(entityType.Name, dbSet?.PropertyName);
    }

    private static EntityRef? EntityFromQueryable(TypeRef receiverType, IAnalysisFacts facts)
    {
        // Receiver is something that implements IQueryable<T>. The T is always
        // the entity. Extract it from the type arguments if present, or walk
        // the receiver's interfaces to find the IQueryable<T>.
        var entityName = ExtractEntityTypeName(receiverType);
        if (entityName is null) return null;

        // Ask the facts provider for the owning DbSet property, if any. This
        // is the principled replacement for the old syntax-walking heuristic:
        // table name derives from the model, not the call-site's text.
        var dbSet = facts.DbSetForEntity(new TypeId(entityName.Id.Value));
        return new EntityRef(entityName.Name, dbSet?.PropertyName);
    }

    private static TypeRef? ExtractEntityTypeName(TypeRef receiverType)
    {
        if (!receiverType.TypeArguments.IsDefaultOrEmpty)
            return receiverType.TypeArguments[0];
        return null;
    }

    private static bool ReturnsQueryable(TypeRef returnType, IAnalysisFacts facts, WellKnownTypes wk)
    {
        if (wk.IQueryable is not { } queryableId) return false;

        var t = Unwrap(returnType, wk);
        return facts.Implements(t.Id, queryableId);
    }

    private static TypeRef Unwrap(TypeRef t, WellKnownTypes wk)
    {
        if (t.TypeArguments.Length == 1 &&
            ((wk.Task is { } taskId && t.Id == taskId) ||
             (wk.ValueTask is { } vtId && t.Id == vtId)))
        {
            return t.TypeArguments[0];
        }
        return t;
    }
}
