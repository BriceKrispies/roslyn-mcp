# CLAUDE.md

Headless Roslyn-powered C# code analysis. Two ways to invoke the same engine: a stdio MCP server for AI clients, or a thin CLI that prints JSON to stdout.

## Layout

```
engine/         class library — all Roslyn analysis lives here
  Services/     IRoslynWorkspaceService, IProjectAnalysisService,
                ICallGraphService, IMediatRMappingService, ICacheService
  Models/       CallGraphModels.cs (call graph DTOs)

engine.cli/     console app — `dotnet run --project engine.cli -- <subcommand>`
  Program.cs    top-level subcommand dispatch, JSON to stdout, logs to stderr

server/         MCP stdio host — references engine, exposes [McpServerTool]s
  Program.cs    Host bootstrap, MCP server wiring
  Tools/        RoslynLspTools.cs (single class, all tool methods)

tests/integration/
  ExampleApp/   MyApp + DependencyApp solution (MediatR + Autofac + EF Core)
  DotNet47App/  legacy .NET Framework smoke target
  ground-truth-callgraph.md   hand-traced reference graph for ExampleApp
  comparison-report.md        engine-vs-ground-truth discrepancy notes
  run-mcp-trace.py            JSON-RPC driver that captures MCP server output
  mcp-output.json             last captured run
```

Target framework: `net10.0`. Roslyn 5.3.0. No solution file — build per project.

## Where to find things

| Looking for | Path |
|---|---|
| Workspace load / MSBuildWorkspace setup | `engine/Services/RoslynWorkspaceService.cs` |
| Symbol / reference / implementation lookups | `engine/Services/ProjectAnalysisService.cs` |
| Caller / callee traversal | `engine/Services/CallGraphService.cs` |
| MediatR request → handler mapping | `engine/Services/MediatRMappingService.cs` |
| On-disk cache (sticky last-loaded solution) | `engine/Services/CacheService.cs` |
| MCP tool definitions (names, descriptions, params) | `server/Tools/RoslynLspTools.cs` |
| CLI subcommand list | `engine.cli/Program.cs` `Dispatch` switch |
| User-facing tool reference | `README.md` |

## Common workflows

**Build everything:**
```
dotnet build server/server.csproj
dotnet build engine.cli/engine.cli.csproj
```

**Use the CLI directly (no MCP needed):**
```
dotnet run --project engine.cli -- load-solution path/to/Foo.sln
dotnet run --project engine.cli -- status
dotnet run --project engine.cli -- mediatr
dotnet run --project engine.cli -- callees <file> <line> <col> [depth] [limit]
```
The last loaded solution is cached on disk (`CacheService`), so subsequent
invocations in the same working directory skip the load.

**Run the MCP server against a client:** see `README.md` for the stdio config.

**Re-run the integration trace:**
```
python tests/integration/run-mcp-trace.py
```
Driver normalizes the framework's PascalCase → snake_case tool name mangling
(e.g. `GetMediatRMappings` → `get_mediat_r_mappings`).

## Adding a new analysis capability

1. Add the method to the relevant service interface + impl in `engine/Services/`.
2. Expose it as an MCP tool in `server/Tools/RoslynLspTools.cs` (use the
   `ExecuteAsync<T>` helper for logging + JSON serialization + error shape).
3. Add a CLI subcommand to the `Dispatch` switch in `engine.cli/Program.cs`
   and document it in the `Usage()` string.

## Known engine gaps

See `tests/integration/comparison-report.md`. Highlights:
- `MediatRMappingService` returns empty `HandlerFilePath` / `HandlerLine`.
- Cross-project MediatR dispatch (MyApp → DependencyApp) isn't traced.
- Call graph explodes LINQ chains into multiple DB ops and misclassifies
  some Read/Write operations on mutations.
- `FindImplementations` is solid (9/9 on the integration fixture).
