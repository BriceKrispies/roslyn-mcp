using System.Collections.Immutable;

namespace Engine.Core.Analysis;

/// <summary>
/// Lightweight type reference. Carries just enough to classify without pulling
/// in the full type graph. Heavier questions (inheritance, interface impl) go
/// through <see cref="IAnalysisFacts"/> so they can be answered lazily.
/// </summary>
public sealed record TypeRef(
    TypeId Id,
    string Name,
    string FullName,
    bool IsInterface,
    ImmutableArray<TypeRef> TypeArguments)
{
    public static readonly TypeRef Unknown =
        new(new TypeId(""), "", "", false, ImmutableArray<TypeRef>.Empty);
}

public sealed record ParameterRef(string Name, TypeRef Type);

public sealed record MethodDescriptor(
    MethodId Id,
    string Name,
    TypeRef ContainingType,
    bool IsAbstract,
    bool IsStatic,
    bool IsExtensionMethod,
    TypeRef ReturnType,
    ImmutableArray<ParameterRef> Parameters,
    MethodId? OriginalDefinitionId,
    SourceLocation? Definition);

/// <summary>
/// A single invocation, resolved to its target symbol and the static type of
/// the receiver expression (critical: for <c>IQueryable&lt;T&gt;.ToList()</c>
/// the receiver is IQueryable even though the method binds to Enumerable).
/// </summary>
public sealed record InvocationDescriptor(
    CallSiteId Site,
    MethodDescriptor Target,
    TypeRef? ReceiverType,
    ImmutableArray<TypeRef> ArgumentTypes,
    SourceLocation Location);

public sealed record DbSetDescriptor(
    string PropertyName,
    TypeId EntityType,
    TypeId ContainingContextType);

public sealed record HandlerDescriptor(
    TypeId RequestType,
    string RequestTypeName,
    string RequestFullName,
    MethodId HandleMethod,
    TypeId HandlerType,
    string HandlerTypeName,
    string HandlerFullName,
    string ResponseTypeName,
    bool IsCommand,
    SourceLocation Definition);
