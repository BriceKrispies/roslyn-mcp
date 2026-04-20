# Ground-Truth Call Graph — ExampleApp

Hand-traced by reading source. Used as the oracle against which `RoslynLspTools` output is diffed.

All paths relative to `tests/integration/ExampleApp/`.

---

## DI Resolution Map

### Default (unnamed) interface → concrete type
| Interface | Resolves to | File:Line |
|---|---|---|
| `IUserService` | `CacheUserService` decorating `DatabaseUserService` | `MyApp/Program.cs:53` |
| `INotificationService` | `EmailNotificationService` | `MyApp/Program.cs:61` |
| `IPaymentProcessor` | `MockPaymentProcessor` (Development) / `StripePaymentProcessor` (other envs) | `MyApp/Program.cs:69-75` |
| `ApplicationDbContext` | EF Core SQLite | `MyApp/Program.cs:26-27` |
| `IMemoryCache` | built-in via `AddMemoryCache` | `MyApp/Program.cs:30` |

### Named registrations (Autofac `.Named<T>("key")`)
- `IUserService`: `"database"` → `DatabaseUserService`, `"mock"` → `MockUserService`, `"cached"` → `CacheUserService(database, IMemoryCache, ILogger)`
- `INotificationService`: `"email"`, `"sms"`, `"push"`
- `IPaymentProcessor`: `"stripe"`, `"paypal"`, `"mock"`

### All implementations per interface (FindImplementations oracle)
- `IUserService`: `DatabaseUserService`, `MockUserService`, `CacheUserService`
- `INotificationService`: `EmailNotificationService`, `SmsNotificationService`, `PushNotificationService`
- `IPaymentProcessor`: `StripePaymentProcessor`, `PayPalPaymentProcessor`, `MockPaymentProcessor`

---

## MediatR Request → Handler Map

| Request | Handler | File:Line of `Handle` |
|---|---|---|
| `MyApp.Messages.HelloMessage` | `MyApp.Handlers.HelloMessageHandler` | `MyApp/Handlers/HelloMessageHandler.cs:16` |
| `MyApp.Messages.GetUsersQuery` | `MyApp.Handlers.GetUsersQueryHandler` | `MyApp/Handlers/GetUsersQueryHandler.cs:24` |
| `MyApp.Messages.CreateUserSessionCommand` | `MyApp.Handlers.CreateUserSessionCommandHandler` | `MyApp/Handlers/CreateUserSessionCommandHandler.cs:20` |
| `MyApp.Messages.GetUserPreferencesQuery` | `MyApp.Handlers.GetUserPreferencesQueryHandler` | `MyApp/Handlers/GetUserPreferencesQueryHandler.cs:16` |
| `MyApp.Messages.IsUserAuthenticatedQuery` | `MyApp.Handlers.IsUserAuthenticatedQueryHandler` | `MyApp/Handlers/IsUserAuthenticatedQueryHandler.cs:16` |
| `MyApp.Messages.LogUserActivityCommand` | `MyApp.Handlers.LogUserActivityCommandHandler` | `MyApp/Handlers/LogUserActivityCommandHandler.cs:20` |
| `MyApp.Messages.PerformMaintenanceCommand` | `MyApp.Handlers.PerformMaintenanceCommandHandler` | `MyApp/Handlers/PerformMaintenanceCommandHandler.cs:26` |
| `MyApp.Messages.ProcessUserActionCommand` | `MyApp.Handlers.ProcessUserActionCommandHandler` | `MyApp/Handlers/ProcessUserActionCommandHandler.cs:18` |
| `MyApp.Messages.UpdateUserProfileCommand` | `MyApp.Handlers.UpdateUserProfileCommandHandler` | `MyApp/Handlers/UpdateUserProfileCommandHandler.cs:26` |
| `MyApp.Messages.ValidateUserPermissionsQuery` | `MyApp.Handlers.ValidateUserPermissionsQueryHandler` | `MyApp/Handlers/ValidateUserPermissionsQueryHandler.cs:16` |
| `DependencyApp.Messages.ValidateUserDataQuery` | `DependencyApp.Handlers.ValidateUserDataQueryHandler` | `DependencyApp/DependencyApp/Handlers/ValidateUserDataQueryHandler.cs:20` |

**Total handlers: 11** (10 in MyApp, 1 in DependencyApp)

---

## Endpoint: `HomeController.Index` (`MyApp/Controllers/HomeController.cs:20`)

### Successful path
1. `_mediator.Send(GetUsersQuery)` → `GetUsersQueryHandler.Handle`
   1. `_mediator.Send(IsUserAuthenticatedQuery)` → `IsUserAuthenticatedQueryHandler.Handle` *(no DB)*
   2. `_mediator.Send(ValidateUserPermissionsQuery)` → `ValidateUserPermissionsQueryHandler.Handle` *(no DB)*
   3. `_mediator.Send(LogUserActivityCommand)` → `LogUserActivityCommandHandler.Handle` → **DB:** `UserActivities.Add` + `SaveChangesAsync`
   4. `_mediator.Send(GetUserPreferencesQuery)` → `GetUserPreferencesQueryHandler.Handle` *(no DB)*
   5. `_mediator.Send(LogUserActivityCommand)` → same as 1.3
   6. **DB:** `Users.OrderBy(...).Take(...).ToListAsync` (`GetUsersQueryHandler.cs:80`)
   7. `_mediator.Send(LogUserActivityCommand)` → same as 1.3

### Permission-denied branch
1. `IsUserAuthenticatedQuery` returns false → handler returns empty *(no DB)*
2. Or: `ValidateUserPermissionsQuery` returns false → log activity → **DB:** `UserActivities.Add` + `SaveChangesAsync`

### Distinct DB ops reachable
- `ApplicationDbContext.Users.ToListAsync` — `GetUsersQueryHandler.cs:80`
- `ApplicationDbContext.UserActivities.Add` — `LogUserActivityCommandHandler.cs:37`
- `ApplicationDbContext.SaveChangesAsync` — `LogUserActivityCommandHandler.cs:38`

---

## Endpoint: `HomeController.ProcessUserAction` (`MyApp/Controllers/HomeController.cs:53`)

Most complex endpoint. Branches on `actionType` prefix; each branch dispatches a different command.

### Top-level structure
1. `_mediator.Send(ProcessUserActionCommand)` → `ProcessUserActionCommandHandler.Handle` (`Handlers/ProcessUserActionCommandHandler.cs:18`)
   - Branches by `actionType` prefix into 4 private methods (`HandleAuthenticationAction`, `HandleProfileAction`, `HandleMaintenanceAction`, `HandleGenericAction`).
   - On success path: sends `LogUserActivityCommand("ACTION_…")`.
2. **Conditional:** `_mediator.Send(LogUserActivityCommand("CONTROLLER_SUCCESS"))` if `result.Success` (controller, line 74).
3. **Conditional:** `_mediator.Send(LogUserActivityCommand("CONTROLLER_FAILURE"))` if `!result.Success` (controller, line 94).
4. **Catch:** `_mediator.Send(LogUserActivityCommand("CONTROLLER_EXCEPTION"))` (controller, line 116).

### Sub-handler dispatch (inside `ProcessUserActionCommandHandler`)
| `actionType` prefix | Dispatches |
|---|---|
| `AUTH_LOGIN` | `_mediator.Send(CreateUserSessionCommand)` |
| `AUTH_LOGOUT` | *(no inner Send)* |
| `PROFILE_UPDATE` | `_mediator.Send(UpdateUserProfileCommand)` |
| `MAINTENANCE_*` (when `isHighPriority`) | `_mediator.Send(PerformMaintenanceCommand)` |
| anything else | none |

### Reachable handlers transitively (full set)
- `ProcessUserActionCommandHandler`
- `CreateUserSessionCommandHandler`
- `UpdateUserProfileCommandHandler` → also sends `ValidateUserDataQuery` → `DependencyApp.Handlers.ValidateUserDataQueryHandler` ⚠️ **cross-project edge**
- `PerformMaintenanceCommandHandler` → sends `LogUserActivityCommand` (begin/complete/error markers)
- `LogUserActivityCommandHandler` *(invoked from controller and from many inner handlers)*

### Distinct DB ops reachable from this endpoint
| DB op | Source |
|---|---|
| `UserSessions.Add` | `CreateUserSessionCommandHandler.cs:49` |
| `SaveChangesAsync` | `CreateUserSessionCommandHandler.cs:50, 80` |
| `UserSessions.Where(...).ToList` | `CreateUserSessionCommandHandler.cs:72` |
| `UserSessions.RemoveRange` | `CreateUserSessionCommandHandler.cs:79` |
| `Users.FirstOrDefaultAsync` | `UpdateUserProfileCommandHandler.cs:41` |
| `SaveChangesAsync` (UPDATE Users) | `UpdateUserProfileCommandHandler.cs:108` |
| `UserActivities.Add` | `LogUserActivityCommandHandler.cs:37` |
| `SaveChangesAsync` | `LogUserActivityCommandHandler.cs:38` |
| `UserActivities.Where(...).ToListAsync` | `PerformMaintenanceCommandHandler.cs:107` |
| `UserActivities.RemoveRange` | `PerformMaintenanceCommandHandler.cs:113` |
| `UserSessions.Where(...).ToListAsync` | `PerformMaintenanceCommandHandler.cs:129, 176` |
| `UserSessions.RemoveRange` | `PerformMaintenanceCommandHandler.cs:136` |
| `Users.FirstOrDefaultAsync` | `PerformMaintenanceCommandHandler.cs:156` |
| `UserActivities.Where(...).OrderByDescending(...).FirstOrDefaultAsync` | `PerformMaintenanceCommandHandler.cs:161` |

---

## Critical things the MCP server's tracer must get right

1. **MediatR dispatch resolution.** Every `_mediator.Send(new XCommand())` must be resolved to the matching `IRequestHandler<XCommand, …>.Handle`. Missing this collapses the whole graph.
2. **Cross-project handler edge.** `UpdateUserProfileCommandHandler` (in `MyApp`) sends a request whose handler lives in `DependencyApp`. The tracer must follow it across the project boundary.
3. **Recursive handler dispatch.** Several handlers send further MediatR requests. Depth must be sufficient (≥ 4) to reach DB ops behind `ProcessUserAction → PerformMaintenance → LogUserActivity → SaveChangesAsync`.
4. **EF Core LINQ chains terminating in async.** `Users.OrderBy(...).Take(...).ToListAsync` is a single DB operation, not a "method chain on a non-DB type".
5. **Autofac decorator unwrapping.** A call to `IUserService.GetUserAsync` should resolve through `CacheUserService` → potentially `DatabaseUserService`. (Note: none of the two traced endpoints currently *use* `IUserService` directly — handlers go straight to `_context`. Decorator behaviour will matter for any future tests that inject `IUserService` into a handler.)
