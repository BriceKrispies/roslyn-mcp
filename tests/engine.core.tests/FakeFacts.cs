using System.Collections.Immutable;
using Engine.Core.Analysis;

namespace Engine.Core.Tests;

/// <summary>
/// Dictionary-backed IAnalysisFacts. Lets tests state the world directly —
/// "method X has these invocations, type Y implements Z" — without standing
/// up a compilation.
/// </summary>
public sealed class FakeFacts : IAnalysisFacts
{
    public WellKnownTypes WellKnown { get; set; } = WellKnownTypes.Empty;

    public Dictionary<MethodId, List<InvocationDescriptor>> Invocations { get; } = new();
    public Dictionary<MethodId, List<MethodId>> Impls { get; } = new();
    public Dictionary<TypeId, HandlerDescriptor> Handlers { get; } = new();
    public Dictionary<TypeId, DbSetDescriptor> DbSetsByEntity { get; } = new();
    public HashSet<(TypeId, TypeId)> InheritanceEdges { get; } = new();
    public HashSet<(TypeId, TypeId)> InterfaceImplements { get; } = new();
    public Dictionary<MethodId, MethodDescriptor> Methods { get; } = new();

    public IEnumerable<InvocationDescriptor> InvocationsIn(MethodId method)
        => Invocations.TryGetValue(method, out var list) ? list : Array.Empty<InvocationDescriptor>();

    public IEnumerable<MethodId> ImplementationsOf(MethodId m)
        => Impls.TryGetValue(m, out var list) ? list : Array.Empty<MethodId>();

    public HandlerDescriptor? HandlerForRequest(TypeId t)
        => Handlers.TryGetValue(t, out var h) ? h : null;

    public DbSetDescriptor? DbSetForEntity(TypeId t)
        => DbSetsByEntity.TryGetValue(t, out var d) ? d : null;

    public bool InheritsFrom(TypeId type, TypeId baseType)
        => type == baseType || InheritanceEdges.Contains((type, baseType));

    public bool Implements(TypeId type, TypeId interfaceType)
        => type == interfaceType || InterfaceImplements.Contains((type, interfaceType));

    public MethodDescriptor? GetMethod(MethodId id)
        => Methods.TryGetValue(id, out var m) ? m : null;
}

/// <summary>Fluent helpers for building FakeFacts succinctly in tests.</summary>
public static class FactsBuilder
{
    public static readonly SourceLocation AnyLoc = new("test.cs", 1, 1);

    public static TypeRef Type(string name, bool isInterface = false, params TypeRef[] typeArgs)
    {
        var id = new TypeId(isInterface ? $"I:{name}" : $"T:{name}");
        var fullName = typeArgs.Length > 0
            ? $"{name}<{string.Join(",", typeArgs.Select(a => a.FullName))}>"
            : name;
        return new TypeRef(id, name, fullName, isInterface, typeArgs.ToImmutableArray());
    }

    public static TypeRef Interface(string name, params TypeRef[] typeArgs)
        => Type(name, isInterface: true, typeArgs);

    public static MethodDescriptor Method(
        string name,
        TypeRef? containingType = null,
        TypeRef? returnType = null,
        bool isExtension = false,
        bool isAbstract = false,
        bool isStatic = false,
        params ParameterRef[] parameters)
    {
        containingType ??= Type("Anon");
        returnType ??= Type("Void");
        var id = new MethodId($"{containingType.FullName}.{name}");
        return new MethodDescriptor(
            id, name, containingType, isAbstract, isStatic, isExtension,
            returnType, parameters.ToImmutableArray(),
            OriginalDefinitionId: null, Definition: AnyLoc);
    }

    public static InvocationDescriptor Invocation(
        MethodDescriptor target,
        TypeRef? receiverType = null,
        params TypeRef[] argumentTypes)
    {
        var siteId = new CallSiteId($"{target.Id}@test");
        return new InvocationDescriptor(
            siteId, target, receiverType,
            argumentTypes.ToImmutableArray(), AnyLoc);
    }
}
