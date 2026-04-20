using Engine.Core.Analysis;
using FluentAssertions;
using static Engine.Core.Tests.FactsBuilder;

namespace Engine.Core.Tests;

public class ClassifierTests
{
    private static WellKnownTypes MediatRWellKnown(out TypeRef iSender, out TypeRef iMediator)
    {
        iSender = Interface("ISender");
        iMediator = Interface("IMediator");
        return new WellKnownTypes(
            IRequestHandler2: new TypeId("I:IRequestHandler`2"),
            IRequestHandler1: new TypeId("I:IRequestHandler`1"),
            ISender: iSender.Id,
            IMediator: iMediator.Id,
            DbContext: null, DbSet: null, IQueryable: null, IEnumerable: null,
            Task: null, ValueTask: null);
    }

    private static WellKnownTypes EfWellKnown(
        out TypeRef dbContext, out TypeRef dbSet, out TypeRef queryable, out TypeRef task)
    {
        dbContext = Type("DbContext");
        dbSet = Type("DbSet`1");
        queryable = Interface("IQueryable`1");
        task = Type("Task`1");
        return new WellKnownTypes(
            IRequestHandler2: null, IRequestHandler1: null,
            ISender: null, IMediator: null,
            DbContext: dbContext.Id,
            DbSet: dbSet.Id,
            IQueryable: queryable.Id,
            IEnumerable: new TypeId("I:IEnumerable`1"),
            Task: task.Id,
            ValueTask: new TypeId("T:ValueTask`1"));
    }

    // --- MediatR ------------------------------------------------------------

    [Fact]
    public void Send_OnISender_WithKnownRequest_ResolvesHandler()
    {
        var wk = MediatRWellKnown(out var iSender, out _);
        var request = Type("CreateUserCommand");
        var handlerType = Type("CreateUserCommandHandler");
        var handleMethod = Method("Handle", containingType: handlerType);

        var handler = new HandlerDescriptor(
            request.Id, "CreateUserCommand", "MyApp.CreateUserCommand",
            handleMethod.Id, handlerType.Id, "CreateUserCommandHandler",
            "MyApp.CreateUserCommandHandler", "Result",
            IsCommand: false, Definition: AnyLoc);

        var facts = new FakeFacts { WellKnown = wk };
        facts.Handlers[request.Id] = handler;

        var send = Method("Send", containingType: iSender, returnType: Type("Task"));
        var inv = Invocation(send, receiverType: iSender, argumentTypes: request);

        var c = Classifier.Classify(inv, facts);

        c.Kind.Should().Be(CallKind.MediatR);
        c.Operation.Should().Be("Send");
        c.Handler.Should().BeSameAs(handler);
    }

    [Fact]
    public void Send_OnIMediator_WithUnknownRequest_StillClassifiedAsMediatR_NoHandler()
    {
        var wk = MediatRWellKnown(out _, out var iMediator);
        var unknownRequest = Type("UnknownRequest");
        var send = Method("Send", containingType: iMediator);

        var c = Classifier.Classify(
            Invocation(send, receiverType: iMediator, argumentTypes: unknownRequest),
            new FakeFacts { WellKnown = wk });

        c.Kind.Should().Be(CallKind.MediatR);
        c.Handler.Should().BeNull();
    }

    [Fact]
    public void Send_OnUnrelatedType_Named_Send_IsPlainMethod()
    {
        var wk = MediatRWellKnown(out _, out _);
        var unrelated = Type("Logger");
        var send = Method("Send", containingType: unrelated);

        Classifier.Classify(Invocation(send, receiverType: unrelated), new FakeFacts { WellKnown = wk })
            .Kind.Should().Be(CallKind.Method);
    }

    // --- EF Core: DbSet methods --------------------------------------------

    [Fact]
    public void DbSet_Add_ClassifiesAsInsert_WithEntityFromGeneric()
    {
        var wk = EfWellKnown(out _, out var dbSet, out _, out _);
        var user = Type("User");
        var dbSetOfUser = Type("DbSet`1", isInterface: false, user);

        // DbSet<User>.Add: ContainingType.Id must be the open-generic DbSet id
        // (this mirrors what the Roslyn shell will emit for consistency).
        var addMethod = Method("Add", containingType: dbSet);

        var c = Classifier.Classify(
            Invocation(addMethod, receiverType: dbSetOfUser),
            new FakeFacts { WellKnown = wk });

        c.Kind.Should().Be(CallKind.Database);
        c.Operation.Should().Be("INSERT");
        c.IsWrite.Should().BeTrue();
        c.Entity!.EntityType.Should().Be("User");
    }

    [Fact]
    public void DbSet_Add_UsesDbSetPropertyName_WhenKnown()
    {
        var wk = EfWellKnown(out _, out var dbSet, out _, out _);
        var user = Type("User");
        var dbSetOfUser = Type("DbSet`1", isInterface: false, user);
        var add = Method("Add", containingType: dbSet);

        var facts = new FakeFacts { WellKnown = wk };
        facts.DbSetsByEntity[user.Id] = new DbSetDescriptor(
            "Users", user.Id, new TypeId("T:ApplicationDbContext"));

        var c = Classifier.Classify(Invocation(add, receiverType: dbSetOfUser), facts);

        c.Kind.Should().Be(CallKind.Database);
        c.Entity!.EntityType.Should().Be("User");
        c.Entity.DbSetName.Should().Be("Users");
        c.Entity.DisplayName.Should().Be("Users");
    }

    [Theory]
    [InlineData("Remove", "DELETE", true)]
    [InlineData("RemoveRange", "DELETE", true)]
    [InlineData("Update", "UPDATE", true)]
    [InlineData("Find", "SELECT", false)]
    [InlineData("FindAsync", "SELECT", false)]
    public void DbSet_Mutators_MapToCorrectOperation(string method, string expectedOp, bool expectedWrite)
    {
        var wk = EfWellKnown(out _, out var dbSet, out _, out _);
        var user = Type("User");
        var dbSetOfUser = Type("DbSet`1", isInterface: false, user);
        var m = Method(method, containingType: dbSet);

        var c = Classifier.Classify(
            Invocation(m, receiverType: dbSetOfUser),
            new FakeFacts { WellKnown = wk });

        c.Kind.Should().Be(CallKind.Database);
        c.Operation.Should().Be(expectedOp);
        c.IsWrite.Should().Be(expectedWrite);
    }

    // --- EF Core: SaveChanges ----------------------------------------------

    [Fact]
    public void SaveChangesAsync_OnDerivedDbContext_IsDatabaseSaveWrite()
    {
        var wk = EfWellKnown(out var dbContext, out _, out _, out _);
        var myCtx = Type("ApplicationDbContext");
        var facts = new FakeFacts { WellKnown = wk };
        facts.InheritanceEdges.Add((myCtx.Id, dbContext.Id));

        var save = Method("SaveChangesAsync", containingType: myCtx);
        var c = Classifier.Classify(Invocation(save, receiverType: myCtx), facts);

        c.Kind.Should().Be(CallKind.Database);
        c.Operation.Should().Be("SAVE");
        c.IsWrite.Should().BeTrue();
    }

    [Fact]
    public void SaveChanges_OnUnrelatedType_IsNotClassified()
    {
        var wk = EfWellKnown(out _, out _, out _, out _);
        var unrelated = Type("ManualMap");
        var save = Method("SaveChanges", containingType: unrelated);

        Classifier.Classify(Invocation(save, receiverType: unrelated), new FakeFacts { WellKnown = wk })
            .Kind.Should().Be(CallKind.Method);
    }

    // --- EF Core: IQueryable extensions ------------------------------------

    [Fact]
    public void ToListAsync_OnIQueryable_IsTerminalSelect_ByReceiverType()
    {
        var wk = EfWellKnown(out _, out _, out var queryable, out var task);
        var user = Type("User");
        var iqUser = Interface("IQueryable`1", user);
        // The receiver expression's static type is IQueryable<User>.
        // ToListAsync binds to EntityFrameworkQueryableExtensions.ToListAsync<T>(IQueryable<T>)
        // which returns Task<List<T>> — not IQueryable<T> — hence terminal.
        var extType = Type("EntityFrameworkQueryableExtensions");
        var listOfUser = Type("List`1", isInterface: false, user);
        var returnType = Type("Task`1", isInterface: false, listOfUser);
        // Normalize return type id so Unwrap recognizes Task<...>.
        returnType = returnType with { Id = task.Id };

        var toListAsync = Method("ToListAsync", containingType: extType,
            returnType: returnType, isExtension: true);

        var facts = new FakeFacts { WellKnown = wk };
        facts.InterfaceImplements.Add((iqUser.Id, queryable.Id));

        var c = Classifier.Classify(Invocation(toListAsync, receiverType: iqUser), facts);

        c.Kind.Should().Be(CallKind.Database);
        c.Operation.Should().Be("SELECT");
        c.Entity!.EntityType.Should().Be("User");
    }

    [Fact]
    public void Where_OnIQueryable_IsIntermediateLinq_AndSkipped()
    {
        var wk = EfWellKnown(out _, out _, out var queryable, out _);
        var user = Type("User");
        var iqUser = Interface("IQueryable`1", user);
        var extType = Type("Queryable");
        // Where returns IQueryable<T> — intermediate, not a DB execution.
        var whereReturn = Interface("IQueryable`1", user) with { Id = queryable.Id };

        var where = Method("Where", containingType: extType,
            returnType: whereReturn, isExtension: true);

        var facts = new FakeFacts { WellKnown = wk };
        facts.InterfaceImplements.Add((iqUser.Id, queryable.Id));
        facts.InterfaceImplements.Add((whereReturn.Id, queryable.Id));

        Classifier.Classify(Invocation(where, receiverType: iqUser), facts)
            .Kind.Should().Be(CallKind.IntermediateLinq);
    }

    [Fact]
    public void ToList_OnIEnumerable_Only_IsPlainMethod_NotDb()
    {
        // In-memory: users.ToList() where users is List<User>.
        var wk = EfWellKnown(out _, out _, out _, out _);
        var user = Type("User");
        var listOfUser = Type("List`1", isInterface: false, user);
        var extType = Type("Enumerable");

        var toList = Method("ToList", containingType: extType,
            returnType: Type("List`1", isInterface: false, user), isExtension: true);

        var facts = new FakeFacts { WellKnown = wk };
        // NOT registered as IQueryable — should fall through to Method.
        Classifier.Classify(Invocation(toList, receiverType: listOfUser), facts)
            .Kind.Should().Be(CallKind.Method);
    }

    [Fact]
    public void ToListAsync_OnLocalIQueryable_DbSetNameFromFactsProvider()
    {
        // The User-vs-Users case. The classifier asks facts for the DbSet
        // property name — no syntax walking needed.
        var wk = EfWellKnown(out _, out _, out var queryable, out var task);
        var user = Type("User");
        var iqUser = Interface("IOrderedQueryable`1", user);
        var returnType = Type("Task`1", isInterface: false,
            Type("List`1", isInterface: false, user)) with { Id = task.Id };
        var toListAsync = Method("ToListAsync",
            containingType: Type("EntityFrameworkQueryableExtensions"),
            returnType: returnType, isExtension: true);

        var facts = new FakeFacts { WellKnown = wk };
        facts.InterfaceImplements.Add((iqUser.Id, queryable.Id));
        facts.DbSetsByEntity[user.Id] = new DbSetDescriptor(
            "Users", user.Id, new TypeId("T:ApplicationDbContext"));

        var c = Classifier.Classify(Invocation(toListAsync, receiverType: iqUser), facts);

        c.Kind.Should().Be(CallKind.Database);
        c.Entity!.EntityType.Should().Be("User");
        c.Entity.DbSetName.Should().Be("Users");
        c.Entity.DisplayName.Should().Be("Users");
    }

    [Fact]
    public void ToListAsync_WhenDbSetUnknown_FallsBackToEntityTypeName()
    {
        var wk = EfWellKnown(out _, out _, out var queryable, out var task);
        var user = Type("User");
        var iqUser = Interface("IQueryable`1", user);
        var returnType = Type("Task`1", isInterface: false,
            Type("List`1", isInterface: false, user)) with { Id = task.Id };
        var toListAsync = Method("ToListAsync",
            containingType: Type("EntityFrameworkQueryableExtensions"),
            returnType: returnType, isExtension: true);

        var facts = new FakeFacts { WellKnown = wk };
        facts.InterfaceImplements.Add((iqUser.Id, queryable.Id));
        // No DbSet registered for User — classifier should still report the entity.

        var c = Classifier.Classify(Invocation(toListAsync, receiverType: iqUser), facts);

        c.Entity!.EntityType.Should().Be("User");
        c.Entity.DbSetName.Should().BeNull();
        c.Entity.DisplayName.Should().Be("User");
    }

    [Fact]
    public void NoMediatRInCompilation_PlainMethodStaysPlain()
    {
        // All WellKnown ids null — classifier should not blow up.
        var wk = WellKnownTypes.Empty;
        var m = Method("Anything");
        Classifier.Classify(Invocation(m), new FakeFacts { WellKnown = wk })
            .Kind.Should().Be(CallKind.Method);
    }
}
