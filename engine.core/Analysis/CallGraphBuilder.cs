using System.Collections.Immutable;

namespace Engine.Core.Analysis;

public sealed record TraversalOptions(int MaxDepth = 5, int Limit = 200);

/// <summary>
/// One entry in the flat callee list. Mirrors the legacy CalleeInfo shape so
/// the shell can translate with no semantic work.
/// </summary>
public sealed record CalleeNode(
    CallSiteId Site,
    MethodDescriptor Target,
    CallKind Kind,
    string? HandlerName,
    string? Operation,
    EntityRef? Entity,
    bool IsWrite,
    int Depth,
    SourceLocation Location);

public sealed record TraversalResult(
    MethodId Entry,
    ImmutableArray<CalleeNode> Callees,
    bool MaxDepthReached)
{
    public IEnumerable<CalleeNode> DatabaseOperations => Callees.Where(c => c.Kind == CallKind.Database);
    public IEnumerable<CalleeNode> MediatRSends       => Callees.Where(c => c.Kind == CallKind.MediatR);
}

/// <summary>
/// Pure call graph traversal. Consumes an <see cref="IAnalysisFacts"/> and
/// produces a flat list of callees with depth annotations. Interface dispatch
/// fans out to all implementations; MediatR sends follow to their handlers;
/// decorator self-recursion is bounded by the visited set.
/// </summary>
public sealed class CallGraphBuilder
{
    private readonly IAnalysisFacts _facts;

    public CallGraphBuilder(IAnalysisFacts facts) { _facts = facts; }

    public TraversalResult Build(MethodId entry, TraversalOptions? options = null)
    {
        options ??= new TraversalOptions();
        var callees = ImmutableArray.CreateBuilder<CalleeNode>();
        var visited = new HashSet<MethodId>();
        var maxReached = new Box<bool>();

        Traverse(entry, callees, visited, options, currentDepth: 0, maxReached);

        return new TraversalResult(entry, callees.ToImmutable(), maxReached.Value);
    }

    private void Traverse(
        MethodId method,
        ImmutableArray<CalleeNode>.Builder callees,
        HashSet<MethodId> visited,
        TraversalOptions options,
        int currentDepth,
        Box<bool> maxReached)
    {
        if (currentDepth >= options.MaxDepth) { maxReached.Value = true; return; }
        if (callees.Count >= options.Limit) return;

        foreach (var inv in _facts.InvocationsIn(method))
        {
            if (callees.Count >= options.Limit) break;

            var classification = Classifier.Classify(inv, _facts);

            if (classification.Kind == CallKind.IntermediateLinq) continue;

            // Dedupe key depends on the effective edge:
            //  * MediatR: by the resolved handler's Handle method, because
            //    ISender.Send is a single symbol dispatched to many handlers.
            //  * Database: no dedupe — each call site is a distinct DB op.
            //  * Method / interface: by the target method id.
            bool alreadySeen = classification.Kind switch
            {
                CallKind.MediatR  => classification.Handler is { } h && !visited.Add(h.HandleMethod),
                CallKind.Database => false,
                _                 => !visited.Add(inv.Target.Id),
            };
            if (alreadySeen) continue;

            callees.Add(new CalleeNode(
                Site: inv.Site,
                Target: inv.Target,
                Kind: classification.Kind,
                HandlerName: classification.Handler?.HandlerTypeName,
                Operation: classification.Operation,
                Entity: classification.Entity,
                IsWrite: classification.IsWrite,
                Depth: currentDepth,
                Location: inv.Location));

            switch (classification.Kind)
            {
                case CallKind.Database:
                    // Don't descend into EF method bodies.
                    continue;

                case CallKind.MediatR:
                    if (classification.Handler is { } handler)
                        Traverse(handler.HandleMethod, callees, visited, options, currentDepth + 1, maxReached);
                    continue;
            }

            // Plain method.
            // Interface / abstract dispatch: fan out to each implementation.
            if (inv.Target.IsAbstract || inv.Target.ContainingType.IsInterface)
            {
                foreach (var impl in _facts.ImplementationsOf(inv.Target.Id))
                {
                    if (callees.Count >= options.Limit) break;
                    if (!visited.Add(impl)) continue;
                    Traverse(impl, callees, visited, options, currentDepth + 1, maxReached);
                }
                continue;
            }

            // Concrete method: recurse.
            Traverse(inv.Target.Id, callees, visited, options, currentDepth + 1, maxReached);
        }
    }

    private sealed class Box<T> { public T Value = default!; }
}
