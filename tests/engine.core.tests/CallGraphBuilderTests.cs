using System.Collections.Immutable;
using Engine.Core.Analysis;
using FluentAssertions;
using static Engine.Core.Tests.FactsBuilder;

namespace Engine.Core.Tests;

/// <summary>
/// Behavior tests for the pure call graph traversal. These drive the builder
/// against hand-written FakeFacts so every rule (interface fan-out, decorator
/// self-recursion, MediatR follow, depth/limit, intermediate LINQ skipping)
/// is exercised in milliseconds without touching Roslyn.
/// </summary>
public class CallGraphBuilderTests
{
    private static MethodId Id(string s) => new(s);

    private static void AddInvocation(FakeFacts facts, MethodDescriptor caller,
        MethodDescriptor target, TypeRef? receiver = null, params TypeRef[] args)
    {
        if (!facts.Invocations.TryGetValue(caller.Id, out var list))
            facts.Invocations[caller.Id] = list = new();
        list.Add(Invocation(target, receiver, args));
    }

    // --- Simple traversal ---------------------------------------------------

    [Fact]
    public void Entry_WithNoInvocations_ProducesEmptyGraph()
    {
        var facts = new FakeFacts();
        var entry = Method("Entry");

        var result = new CallGraphBuilder(facts).Build(entry.Id);

        result.Callees.Should().BeEmpty();
        result.MaxDepthReached.Should().BeFalse();
    }

    [Fact]
    public void ConcreteCallChain_RecursesUntilLeaf()
    {
        var facts = new FakeFacts();
        var a = Method("A");
        var b = Method("B");
        var c = Method("C");
        AddInvocation(facts, a, b);
        AddInvocation(facts, b, c);

        var result = new CallGraphBuilder(facts).Build(a.Id, new TraversalOptions(MaxDepth: 5));

        result.Callees.Should().HaveCount(2);
        result.Callees[0].Target.Name.Should().Be("B");
        result.Callees[0].Depth.Should().Be(0);
        result.Callees[1].Target.Name.Should().Be("C");
        result.Callees[1].Depth.Should().Be(1);
    }

    [Fact]
    public void DepthLimit_StopsBeforeReachingGrandchild()
    {
        var facts = new FakeFacts();
        var a = Method("A");
        var b = Method("B");
        var c = Method("C");
        AddInvocation(facts, a, b);
        AddInvocation(facts, b, c);

        var result = new CallGraphBuilder(facts).Build(a.Id, new TraversalOptions(MaxDepth: 1));

        // Depth 0 callees only: B is recorded, but we don't recurse into it.
        result.Callees.Should().ContainSingle(n => n.Target.Name == "B");
        result.Callees.Should().NotContain(n => n.Target.Name == "C");
        result.MaxDepthReached.Should().BeTrue();
    }

    [Fact]
    public void Limit_TruncatesCalleeList()
    {
        var facts = new FakeFacts();
        var a = Method("A");
        for (var i = 0; i < 10; i++)
            AddInvocation(facts, a, Method($"B{i}"));

        var result = new CallGraphBuilder(facts).Build(a.Id, new TraversalOptions(Limit: 3));

        result.Callees.Should().HaveCount(3);
    }

    [Fact]
    public void VisitedSet_PreventsInfiniteRecursion()
    {
        var facts = new FakeFacts();
        var a = Method("A");
        var b = Method("B");
        AddInvocation(facts, a, b);
        AddInvocation(facts, b, a); // cycle

        var result = new CallGraphBuilder(facts).Build(a.Id, new TraversalOptions(MaxDepth: 10));

        result.Callees.Should().HaveCount(2);
    }

    // --- Interface dispatch -------------------------------------------------

    [Fact]
    public void InterfaceCall_FansOutToAllImplementations()
    {
        var facts = new FakeFacts();
        var iFoo = Interface("IFoo");
        var bar = Method("Bar", containingType: iFoo, isAbstract: true);

        var implA = Method("Bar", containingType: Type("A"));
        var implB = Method("Bar", containingType: Type("B"));

        var leafA = Method("Console.WriteA");
        var leafB = Method("Console.WriteB");
        AddInvocation(facts, implA, leafA);
        AddInvocation(facts, implB, leafB);

        facts.Impls[bar.Id] = new() { implA.Id, implB.Id };

        var entry = Method("Entry");
        AddInvocation(facts, entry, bar, receiver: iFoo);

        var result = new CallGraphBuilder(facts).Build(entry.Id, new TraversalOptions(MaxDepth: 5));

        result.Callees.Select(n => n.Target.Name).Should()
            .Contain(new[] { "Bar", "Console.WriteA", "Console.WriteB" });
    }

    [Fact]
    public void DecoratorChain_TerminatesWithoutInfiniteLoop()
    {
        // Decorator: Decorator.Bar calls IFoo.Bar internally. The inner is
        // IFoo.Bar again — when dispatched, would fan out to Decorator + Concrete.
        // Visited set must block re-entry into Decorator.Bar.
        var facts = new FakeFacts();
        var iFoo = Interface("IFoo");
        var bar = Method("Bar", containingType: iFoo, isAbstract: true);

        var decorator = Method("Bar", containingType: Type("Decorator"));
        var concrete = Method("Bar", containingType: Type("Concrete"));
        var leaf = Method("DbSet.Find");

        // Decorator.Bar calls IFoo.Bar (the interface) — that's the decorator pattern.
        AddInvocation(facts, decorator, bar, receiver: iFoo);
        AddInvocation(facts, concrete, leaf);

        facts.Impls[bar.Id] = new() { decorator.Id, concrete.Id };

        var entry = Method("Entry");
        AddInvocation(facts, entry, bar, receiver: iFoo);

        var result = new CallGraphBuilder(facts).Build(entry.Id, new TraversalOptions(MaxDepth: 10));

        // Should reach the leaf via Concrete.Bar, and not explode.
        result.Callees.Should().Contain(n => n.Target.Name == "DbSet.Find");
    }

    // --- MediatR follow -----------------------------------------------------

    [Fact]
    public void MediatRSend_ResolvesHandler_RecursesIntoHandle()
    {
        var wk = new WellKnownTypes(
            IRequestHandler2: new TypeId("I:IRequestHandler`2"),
            IRequestHandler1: null,
            ISender: new TypeId("I:ISender"), IMediator: new TypeId("I:IMediator"),
            DbContext: null, DbSet: null, IQueryable: null, IEnumerable: null,
            Task: null, ValueTask: null);
        var facts = new FakeFacts { WellKnown = wk };

        var iSender = Interface("ISender") with { Id = wk.ISender!.Value };
        var request = Type("CreateUserCommand");
        var handlerType = Type("CreateUserCommandHandler");
        var handle = Method("Handle", containingType: handlerType);
        var leaf = Method("DbSet.Add");
        AddInvocation(facts, handle, leaf);

        facts.Handlers[request.Id] = new HandlerDescriptor(
            request.Id, "CreateUserCommand", "N.CreateUserCommand",
            handle.Id, handlerType.Id, "CreateUserCommandHandler",
            "N.CreateUserCommandHandler", "Unit", IsCommand: true, Definition: AnyLoc);

        var entry = Method("Controller");
        var sendMethod = Method("Send", containingType: iSender);
        AddInvocation(facts, entry, sendMethod, receiver: iSender, args: request);

        var result = new CallGraphBuilder(facts).Build(entry.Id, new TraversalOptions(MaxDepth: 5));

        result.Callees.Should().Contain(n => n.Kind == CallKind.MediatR && n.HandlerName == "CreateUserCommandHandler");
        result.Callees.Should().Contain(n => n.Target.Name == "DbSet.Add");
    }

    // --- EF stop-at-DB ------------------------------------------------------

    [Fact]
    public void DatabaseOperation_NotRecursedInto()
    {
        var wk = new WellKnownTypes(null, null, null, null,
            DbContext: new TypeId("T:DbContext"),
            DbSet: new TypeId("T:DbSet`1"),
            IQueryable: null, IEnumerable: null, Task: null, ValueTask: null);
        var facts = new FakeFacts { WellKnown = wk };

        var entry = Method("H");
        var dbSet = Type("DbSet`1") with { Id = wk.DbSet!.Value };
        var dbSetOfUser = Type("DbSet`1", isInterface: false, Type("User")) with { Id = wk.DbSet!.Value };
        var addMethod = Method("Add", containingType: dbSet);

        // Simulate that DbSet.Add has a body with a nested call we should NOT descend into.
        var nestedInternal = Method("InternalAddHelper");
        AddInvocation(facts, addMethod, nestedInternal);
        AddInvocation(facts, entry, addMethod, receiver: dbSetOfUser);

        var result = new CallGraphBuilder(facts).Build(entry.Id, new TraversalOptions(MaxDepth: 5));

        result.Callees.Should().ContainSingle(n => n.Kind == CallKind.Database && n.Operation == "INSERT");
        result.Callees.Should().NotContain(n => n.Target.Name == "InternalAddHelper");
    }

    // --- Intermediate LINQ --------------------------------------------------

    // --- Round trip --------------------------------------------------------

    [Fact]
    public void FullChain_ControllerSendsMediatR_HandlerCallsDecoratorChain_WritesToDb()
    {
        // Entry (Controller.Action)
        //   └ ISender.Send(Command)                         [MediatR]
        //       └ CommandHandler.Handle                     [recurse]
        //           └ IUserService.Save                     [interface fanout]
        //               ├ CacheUserService.Save             [decorator] → calls IUserService.Save again (cycle)
        //               └ DatabaseUserService.Save          [concrete]
        //                   └ DbSet<User>.Add               [Database INSERT, Entity=Users]
        var dbSetId = new TypeId("T:DbSet`1");
        var wk = new WellKnownTypes(
            IRequestHandler2: new TypeId("I:IRequestHandler`2"), IRequestHandler1: null,
            ISender: new TypeId("I:ISender"), IMediator: null,
            DbContext: new TypeId("T:DbContext"), DbSet: dbSetId,
            IQueryable: null, IEnumerable: null, Task: null, ValueTask: null);
        var facts = new FakeFacts { WellKnown = wk };

        var iSender = Interface("ISender") with { Id = wk.ISender!.Value };
        var dbSet = Type("DbSet`1") with { Id = dbSetId };
        var user = Type("User");
        var dbSetOfUser = Type("DbSet`1", isInterface: false, user) with { Id = dbSetId };
        facts.DbSetsByEntity[user.Id] = new DbSetDescriptor(
            "Users", user.Id, new TypeId("T:AppDbContext"));

        // IUserService with decorator + concrete impls.
        var iUserService = Interface("IUserService");
        var saveAbstract = Method("Save", containingType: iUserService, isAbstract: true);
        var cacheImpl = Method("Save", containingType: Type("CacheUserService"));
        var dbImpl = Method("Save", containingType: Type("DatabaseUserService"));
        facts.Impls[saveAbstract.Id] = new() { cacheImpl.Id, dbImpl.Id };
        AddInvocation(facts, cacheImpl, saveAbstract, receiver: iUserService);     // decorator self-ref
        var add = Method("Add", containingType: dbSet);
        AddInvocation(facts, dbImpl, add, receiver: dbSetOfUser);

        // Handler.
        var cmdType = Type("CreateUserCommand");
        var handlerType = Type("CreateUserCommandHandler");
        var handle = Method("Handle", containingType: handlerType);
        AddInvocation(facts, handle, saveAbstract, receiver: iUserService);
        facts.Handlers[cmdType.Id] = new HandlerDescriptor(
            cmdType.Id, "CreateUserCommand", "N.CreateUserCommand",
            handle.Id, handlerType.Id, "CreateUserCommandHandler",
            "N.CreateUserCommandHandler", "Unit", IsCommand: true, Definition: AnyLoc);

        // Controller entry.
        var controller = Method("Create", containingType: Type("UserController"));
        var send = Method("Send", containingType: iSender);
        AddInvocation(facts, controller, send, receiver: iSender, args: cmdType);

        var result = new CallGraphBuilder(facts).Build(controller.Id, new TraversalOptions(MaxDepth: 10));

        result.MediatRSends.Should().ContainSingle()
            .Which.HandlerName.Should().Be("CreateUserCommandHandler");

        var dbOp = result.DatabaseOperations.Should().ContainSingle().Subject;
        dbOp.Operation.Should().Be("INSERT");
        dbOp.Entity!.DisplayName.Should().Be("Users");

        // All four methods reached: Send, Save (interface), CacheUserService.Save, DatabaseUserService.Save, Add.
        result.Callees.Select(c => c.Target.Name).Should()
            .Contain(new[] { "Send", "Save", "Add" });
        // Decorator self-ref didn't explode — visited blocks re-entry into IUserService.Save.
        result.Callees.Count(c => c.Target.Name == "Save" && c.Kind == CallKind.Method)
            .Should().BeLessThanOrEqualTo(3); // interface Save + two impls, no duplicates from cycle.
    }

    [Fact]
    public void IntermediateLinq_IsSkippedEntirely()
    {
        var queryableId = new TypeId("I:IQueryable`1");
        var wk = new WellKnownTypes(null, null, null, null,
            DbContext: new TypeId("T:DbContext"),
            DbSet: new TypeId("T:DbSet`1"),
            IQueryable: queryableId,
            IEnumerable: new TypeId("I:IEnumerable`1"),
            Task: null, ValueTask: null);
        var facts = new FakeFacts { WellKnown = wk };

        var user = Type("User");
        var iqUser = Interface("IQueryable`1", user);
        var whereReturn = Interface("IQueryable`1", user) with { Id = queryableId };
        var where = Method("Where",
            containingType: Type("Queryable"),
            returnType: whereReturn,
            isExtension: true);
        facts.InterfaceImplements.Add((iqUser.Id, queryableId));
        facts.InterfaceImplements.Add((whereReturn.Id, queryableId));

        var entry = Method("H");
        AddInvocation(facts, entry, where, receiver: iqUser);

        var result = new CallGraphBuilder(facts).Build(entry.Id, new TraversalOptions(MaxDepth: 5));
        result.Callees.Should().BeEmpty();
    }
}
