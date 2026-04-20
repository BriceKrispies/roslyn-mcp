namespace Engine.Core.Analysis;

/// <summary>
/// Opaque stable identity for a method symbol. Shell derives from
/// ISymbol.GetDocumentationCommentId() (Roslyn guarantees stability).
/// Core treats it as an opaque key.
/// </summary>
public readonly record struct MethodId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Opaque stable identity for a type symbol.</summary>
public readonly record struct TypeId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>Identity for a particular call site (file/line/col encoded).</summary>
public readonly record struct CallSiteId(string Value)
{
    public override string ToString() => Value;
}

public sealed record SourceLocation(string FilePath, int Line, int Column);
