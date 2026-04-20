using Microsoft.CodeAnalysis;

namespace Engine.Services;

internal sealed record MediatRSymbols(
    INamedTypeSymbol? IRequestHandler2,
    INamedTypeSymbol? IRequestHandler1,
    INamedTypeSymbol? ISender,
    INamedTypeSymbol? IMediator)
{
    public bool HasMediatR => IRequestHandler2 is not null || IRequestHandler1 is not null;

    public static MediatRSymbols? From(Compilation c)
    {
        var s = new MediatRSymbols(
            c.GetTypeByMetadataName("MediatR.IRequestHandler`2"),
            c.GetTypeByMetadataName("MediatR.IRequestHandler`1"),
            c.GetTypeByMetadataName("MediatR.ISender"),
            c.GetTypeByMetadataName("MediatR.IMediator"));
        return s.HasMediatR ? s : null;
    }
}

internal sealed record EfCoreSymbols(
    INamedTypeSymbol DbContext,
    INamedTypeSymbol DbSet,
    INamedTypeSymbol? QueryableExtensions,
    INamedTypeSymbol? Enumerable,
    INamedTypeSymbol? Queryable)
{
    public static EfCoreSymbols? From(Compilation c)
    {
        var ctx = c.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
        var set = c.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbSet`1");
        if (ctx is null || set is null) return null;
        return new EfCoreSymbols(
            ctx, set,
            c.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"),
            c.GetTypeByMetadataName("System.Linq.Enumerable"),
            c.GetTypeByMetadataName("System.Linq.Queryable"));
    }

    public bool DerivesFromDbContext(ITypeSymbol type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, DbContext)) return true;
        }
        return false;
    }
}
