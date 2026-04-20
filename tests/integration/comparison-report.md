# MCP Server vs Ground-Truth — Discrepancy Report

Source data:
- Ground truth: `tests/integration/ground-truth-callgraph.md` (hand-traced)
- Server output: `tests/integration/mcp-output.json` (driven by `run-mcp-trace.py`)

---

## Score card

| Capability | Result |
|---|---|
| MediatR request → handler **mapping** (`GetMediatRMappings`) | ✅ all 11 handlers found, including the cross-project one |
| MediatR handler **locations** in mapping output | ❌ `filePath = ""`, `line = 0` for every handler |
| `FindImplementations` for the 3 abstracted services | ✅ 3/3, 3/3, 3/3 — perfect |
| `FindCalleesFromLocation` reaches MediatR handler bodies | ⚠️ partially — yes for same-project, **no for cross-project** |
| `ExternalCalls.MediatR` lists which handlers are reached | ⚠️ 5 of 6 handlers reached are listed; `ValidateUserDataQueryHandler` missing |
| EF Core LINQ chain → single logical DB op | ❌ each LINQ method becomes a separate "DB op" |
| `Table` field on DB ops | ❌ frequently the *method name* (`SaveChangesAsync`, `ToListAsync`, `FirstOrDefaultAsync`, `OrderByDescending`) |
| `Type` field (Read/Write) | ❌ `RemoveRange` reported as `Read`; `SaveChangesAsync` reported as `Read` |
| Autofac decorator chain awareness | ➖ untestable here — no traced endpoint actually uses `IUserService` directly |

---

## 1. MediatR mapping — handler locations are blank

`GetMediatRMappings` returns 11 mappings (correct count, including `ValidateUserDataQuery` in `DependencyApp`). But every entry has:

```json
"handlerLocation": { "filePath": "", "line": 0, "column": 0 }
```

A client cannot navigate to the handler. **Bug in `MediatRMappingService`** — it resolves the symbol but never extracts the syntax-tree location. Fix: when collecting handlers, also walk `IMethodSymbol.Locations` for the `Handle` method and emit the file/line/column.

## 2. Cross-project MediatR edge is NOT traced into

`HomeController.ProcessUserAction` → (MediatR) → `UpdateUserProfileCommandHandler.Handle` (in `MyApp`) → (MediatR) → `ValidateUserDataQueryHandler.Handle` (in **`DependencyApp`**).

The server reaches `UpdateUserProfileCommandHandler` (we see its `FirstOrDefaultAsync` at line 41 in the DB-ops list) but it does **not** descend into `ValidateUserDataQueryHandler`. Evidence:
- `ExternalCalls.MediatR.Operations` lists 5 handler classes but **omits** `ValidateUserDataQueryHandler`.
- No callee from the `DependencyApp` project shows up at any depth.

This is the most important MediatR finding: same-assembly resolution works, **cross-assembly does not**. Likely cause in `MediatRMappingService`/`CallGraphService`: the request → handler lookup is built per-compilation rather than across the whole solution, so requests whose handler lives in a different `Compilation` aren't followed.

## 3. ExternalCalls.MediatR has noise

Alongside the resolved handler class names, the list includes a literal `"Send"`. That's the abstract method name leaking into the resolved-handler list. Should be filtered out.

## 4. EF Core LINQ chains are exploded into multiple "DB operations"

Source (`PerformMaintenanceCommandHandler.cs:161`):
```csharp
var lastActivity = await _context.UserActivities
    .Where(a => a.UserId == userIdInt)
    .OrderByDescending(a => a.Timestamp)
    .FirstOrDefaultAsync(...);
```

Ground truth: **one** DB op (a `SELECT … ORDER BY … LIMIT 1`).
Server reports **two** entries at line 161:
```
{ Operation: SELECT,  Table: 'FirstOrDefaultAsync',  Method: FirstOrDefaultAsync }
{ Operation: QUERY,   Table: 'OrderByDescending',    Method: OrderByDescending }
```

Same problem on the `GetUsersQueryHandler.cs:75-80` chain — server emits 3 entries (`OrderBy`, `ToListAsync`, plus an erroneous `Select`).

## 5. `Table` is wrong on terminal LINQ methods

The detection logic seems to use the receiver's identifier, but for chained methods that receiver is the result of the previous method, not the `DbSet`. So:
- `_context.UserActivities.Where(...).ToListAsync(...)` → `Table: "ToListAsync"` instead of `"UserActivities"`.
- `_context.SaveChangesAsync()` → `Table: "SaveChangesAsync"`.

The correct table is the root `DbSet<T>` symbol — walk to the leftmost member access.

## 6. `Type` (Read/Write) classification is wrong for mutations

| Method | Reported `Type` | Should be |
|---|---|---|
| `DbSet.RemoveRange` | `Read` | `Write` (DELETE) |
| `DbContext.SaveChangesAsync` | `Read` | `Write` (or N/A) |
| `Queryable.Where` | `Read` | not a DB op on its own |

## 7. `Enumerable.Select` mis-classified as a DB op

`Index` reports `{ Method: 'Select', Table: 'Select', line 87 }` — that's the in-memory projection `users.Select(u => new {...})` *after* `ToListAsync`, which executes client-side. The classifier should distinguish `IQueryable<T>` extensions from `IEnumerable<T>` extensions.

## 8. Spurious DB op for plain `SaveChangesAsync` line 50

A `SaveChangesAsync` at line 50 of `CreateUserSessionCommandHandler.cs` is reported, but the same DB op at line 38 of `LogUserActivityCommandHandler.cs` (also reachable from `ProcessUserAction`) is **missing** from the deduped list — the dedup logic is keyed by `(Method, Location)` but it should preserve every distinct `(file, line)`.

## 9. Tool-name mangling

`[McpServerTool]` PascalCase → snake_case conversion in the framework produced `get_mediat_r_mappings` (note the `_r_`). Cosmetic, but ugly. Either rename the C# method (`GetMediatrMappings`) or override the tool name explicitly via the attribute.

---

## What's actually solid

- Workspace loads 2 projects in the solution.
- `FindImplementations` returns all concrete implementations for `IUserService`, `INotificationService`, `IPaymentProcessor` with correct file paths.
- Same-project MediatR `Send` calls **are** followed transitively — the recursion goes 4 levels deep (controller → outer handler → inner handler → DB call) without hitting the depth limit on its own.
- The MediatR request → handler map itself is complete (just lacks locations).

---

## Suggested fix priorities

1. **Cross-project MediatR resolution** (§2). Without this, handlers in shared libraries are invisible — that's the central use case the example is designed around.
2. **Empty `handlerLocation` in `GetMediatRMappings`** (§1). Trivial fix, high value.
3. **`Table` extraction walks the wrong receiver** (§5). One bug fixes §4, §5, §7 simultaneously.
4. `RemoveRange`/`SaveChangesAsync` Type classification (§6).
5. Tool-name conversion / `MediatR` casing (§9).

Autofac decorator-chain resolution couldn't be tested with these endpoints — no traced path goes through `IUserService`. Worth adding a controller method that injects `IUserService` directly to exercise that.
