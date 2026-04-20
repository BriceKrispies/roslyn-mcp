# .NET LSP MCP Server

Roslyn-powered C# code analysis with two entry points: a stdio MCP server for AI clients, and a thin CLI that prints JSON to stdout. Both drive the same engine.

## Installation

### Prerequisites
- A .NET SDK installed (any recent version). The shipped binary is self-contained
  for the .NET *runtime*, but `MSBuildWorkspace` still loads MSBuild assemblies
  from an installed SDK at runtime. `Microsoft.Build.Locator` picks whichever
  one it finds.
- A compatible MCP client (Claude Code, Cursor, Claude Desktop, etc.) — or just use the CLI.

### Quick install (recommended)

```bash
# macOS / Linux
curl -fsSL https://raw.githubusercontent.com/BriceKrispies/roslyn-mcp/main/install.sh | bash

# Windows (PowerShell)
iwr https://raw.githubusercontent.com/BriceKrispies/roslyn-mcp/main/install.ps1 -OutFile install.ps1; ./install.ps1
```

By default this installs the latest self-contained binary to a user-level
location (`~/.local/bin/` on Unix, `%LOCALAPPDATA%\Programs\dotnet-lsp-mcp\`
on Windows) and registers it with Claude Code at **user scope**, so the
`dotnet-lsp` server is available in every project without per-project setup.
If the `claude` CLI isn't on PATH, the script prints the `claude mcp add`
command to run manually.

For a **project-scoped** install (writes `.mcp.json` to the current directory
instead), run the script with `--project` / `-Project`. Useful if you want to
pin a specific binary to a specific repo.

Env overrides: `REPO=<owner>/<repo>` to install from a fork, `INSTALL_DIR=<path>`
to override the binary location.

### Updates

Re-run the install script any time to pull the latest, or use the built-in
self-update:

```bash
dotnet-lsp-mcp update
```

On startup the server also checks GitHub releases in the background and logs
a nudge to stderr if a newer version is available (visible in Claude Code's
MCP server logs).

### Build from source

```bash
dotnet build server/server.csproj
dotnet build engine.cli/engine.cli.csproj
```

### Manual MCP client config

```json
{
  "mcpServers": {
    "dotnet-lsp": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "path/to/dotnet-lsp-mcp/server/server.csproj"]
    }
  }
}
```

### Direct CLI (no MCP client needed)

```bash
dotnet run --project engine.cli -- load-solution path/to/Foo.sln
dotnet run --project engine.cli -- status
dotnet run --project engine.cli -- mediatr
dotnet run --project engine.cli -- callees <file> <line> <col> [depth] [limit]
dotnet run --project engine.cli -- callers <file> <line> <col> [depth] [limit]
dotnet run --project engine.cli -- impls <interfaceName> [projectName]
dotnet run --project engine.cli -- refs <symbol> [projectName]
dotnet run --project engine.cli -- symbols <projectName> [symbolKind]
dotnet run --project engine.cli -- diagnostics [projectName]
dotnet run --project engine.cli -- analyze-project <projectPath>
dotnet run --project engine.cli -- analyze-solution <solutionPath>
dotnet run --project engine.cli -- analyze-file <filePath>
```

The last loaded solution is cached on disk, so subsequent invocations in the same working directory skip the load.

## MCP Tools

| Tool | Description | Parameters |
|------|-------------|------------|
| **LoadSolution** | Load a .NET solution and prepare it for analysis | `solutionPath` |
| **LoadProject** | Load a single .NET project | `projectPath` |
| **AnalyzeProject** | Comprehensive analysis of a project (diagnostics, symbols, metrics) | `projectPath` |
| **AnalyzeSolution** | Analysis across all projects in a solution | `solutionPath` |
| **AnalyzeFile** | Detailed analysis of a file (symbols, diagnostics, structure) | `filePath` |
| **GetSymbols** | All symbols of a specified kind from a project | `projectName`, `symbolKind` (Class, Method, Property, ...) |
| **FindReferences** | All references to a symbol across the workspace | `symbol`, `projectName?` |
| **FindImplementations** | All implementations of an interface | `interfaceName`, `projectName?` |
| **GetDiagnostics** | Compiler diagnostics for a project or the whole workspace | `projectName?` |
| **GetWorkspaceStatus** | Currently loaded projects and their state | — |
| **ClearWorkspace** | Clear the workspace and invalidate caches | — |
| **FindCallersFromLocation** | All callers (controllers/endpoints) that reach a given location | `filePath`, `line`, `column`, `maxDepth=5`, `limit=100` |
| **FindCalleesFromLocation** | Methods + DB ops + MediatR sends reachable from a location | `filePath`, `line`, `column`, `maxDepth=5`, `limit=200` |
| **GetMediatRMappings** | MediatR request → handler map | — |
| **EchoSolutionPath** | Echoes the `SolutionPath` env var | — |

## Call graph analysis

`FindCalleesFromLocation` is the richest tool. It does more than walk invocations:

- **MediatR follow.** `_mediator.Send(new CreateUserCommand())` is resolved to the matching `IRequestHandler.Handle` and the handler body is traversed. Works across project boundaries.
- **Interface / decorator fan-out.** A call through an interface fans out to every concrete implementation via `SymbolFinder.FindImplementationsAsync`, so Autofac decorator chains (`IUserService` → `CacheUserService` → `DatabaseUserService`) are followed all the way to the leaf.
- **EF Core classification.** `DbSet<T>.Add/Remove/Update/Find` map to `INSERT/DELETE/UPDATE/SELECT`. `IQueryable<T>` terminal methods (`ToListAsync`, `FirstOrDefaultAsync`, `CountAsync`, …) are classified as DB reads; intermediate LINQ (`Where`, `Select`, `OrderBy` returning `IQueryable<T>`) is skipped. The shell resolves entity types to their `DbSet<T>` property name, so you get `Users` / `UserActivities` / `UserSessions` instead of the bare CLR type.
- **Cycle-safe.** A visited set prevents decorator self-recursion and general cycles from exploding the traversal.

## Architecture

```
engine.core/        pure library — no Roslyn references
  Analysis/         Classifier, CallGraphBuilder, IAnalysisFacts port,
                    WellKnownTypes, Descriptors, Ids
  Models/           CallGraphModels.cs (DTOs)

engine/             Roslyn shell
  Services/         RoslynWorkspaceService, ProjectAnalysisService,
                    CallGraphService, MediatRMappingService, CacheService,
                    RoslynAnalysisFacts (adapter: Roslyn → IAnalysisFacts)

engine.cli/         console app — stdout JSON dispatcher
server/             MCP stdio host — exposes [McpServerTool]s

tests/engine.core.tests/   xUnit + FluentAssertions behavior tests for the core
tests/integration/         ExampleApp fixture (MediatR + Autofac + EF Core),
                           ground-truth-callgraph.md, run-mcp-trace.py
```

The core is a pure functional core: `Classifier.Classify(invocation, facts)` and `new CallGraphBuilder(facts).Build(entry, options)` are both pure functions that know nothing about Roslyn. All compilation-dependent questions (what does this method call? what implements this interface? what DbContext holds this entity?) are answered by the `IAnalysisFacts` port, which the shell (`RoslynAnalysisFacts`) implements lazily on top of Roslyn. Everything classifier-level can be unit-tested against hand-written `FakeFacts` without standing up a compilation.

## Tests

```bash
dotnet test tests/engine.core.tests/engine.core.tests.csproj
```

29 behavior tests cover: every classifier rule (MediatR send, DbSet mutators, SaveChanges on derived DbContext, terminal vs. intermediate LINQ, in-memory LINQ, table-name resolution with and without registered DbSet), and every traversal rule (depth/limit, cycle dedup, interface fan-out, decorator termination, MediatR follow, EF stop-at-DB, cross-project round-trip).

For integration verification: `python tests/integration/run-mcp-trace.py` drives the MCP server end-to-end against `ExampleApp` and writes JSON to `mcp-output.json`. `ground-truth-callgraph.md` is the hand-traced oracle — diff against it.
