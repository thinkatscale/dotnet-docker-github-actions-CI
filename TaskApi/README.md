# TaskApi: modern C# in 1 hour (for a C# 5 / Web API 2 developer)

## Setup (5 min)
1. Install the .NET 10 SDK: https://dotnet.microsoft.com/download
2. `cd TaskApi && dotnet run`
3. Note the port printed in the console. Edit `@host` in `TaskApi.http` to match.
4. Use VS Code (REST Client ext.), Visual Studio, or Rider to send the requests in `TaskApi.http`.

Tip: `dotnet watch` reloads on save.

## 1-hour plan
| Min | Do this | What you learn |
|-----|---------|----------------|
| 0-5 | Run it, send every request in `TaskApi.http` | The API works end to end |
| 5-15 | Read `Models.cs` | records, file-scoped namespaces, nullable reference types (`string?`) |
| 15-30 | Read `TaskService.cs` | primary constructors, collection expressions `[.. x]`, `with`, `is not null`, `??`, structured logging |
| 30-45 | Read `Program.cs` | top-level statements, Minimal APIs, `MapGroup`, `TypedResults`, `Results<...>`, DI, middleware, ProblemDetails, OpenAPI |
| 45-60 | Do the exercises below | Retention |

## Cheat sheet: your world vs. now
| C# 5 / Web API 2 | Modern |
|---|---|
| `Global.asax`, `Startup.cs`, `WebApiConfig` | `Program.cs` with `WebApplication.CreateBuilder` |
| `ApiController` + `[HttpGet]` | `app.MapGet(...)` (or controllers, still supported) |
| `IHttpActionResult` | `TypedResults.Ok(...)`, `Results<Ok<T>, NotFound>` |
| Unity / Autofac | Built-in `builder.Services.AddSingleton/Scoped/Transient` |
| `DelegatingHandler`, filters | Middleware (`app.Use...`) |
| `web.config` | `appsettings.json` + `IConfiguration` / Options pattern |
| Hand-written DTO classes | `record` |
| `if (x != null)` | `if (x is not null)`, `x?.Prop`, `x ?? y` |
| `new List<int> { 1, 2 }` | `List<int> x = [1, 2];` |
| `DateTime.UtcNow` | `TimeProvider` (injectable, testable) |
| Newtonsoft.Json | `System.Text.Json` (built in, default) |
| .NET Framework, IIS only | .NET 10, cross-platform, Kestrel, `dotnet` CLI |

## Exercises (last 15 min)
1. Add `DueDate` (`DateOnly?`) to the model, create and update requests.
2. Add `GET /api/tasks/stats` returning counts per priority. Hint: `GroupBy`, and a `switch` expression to label them.
3. Add a `Tags` list (`List<string>`). Use collection expressions and `request.Tags ?? []`.
4. Make the service `Scoped` and back it with EF Core + SQLite (`dotnet add package Microsoft.EntityFrameworkCore.Sqlite`). The interface is already async, so endpoints don't change.
5. Add a tiny test project (`dotnet new xunit`) using `WebApplicationFactory<Program>`. Add `public partial class Program;` at the end of `Program.cs`.

## Next topics (after this hour)
Options pattern and `appsettings.json`, JWT auth (`AddAuthentication().AddJwtBearer()`), EF Core, `IAsyncEnumerable<T>`, `async`/`await` with `CancellationToken`, `required` members, `switch` expressions and list patterns, LINQ additions (`CountBy`, `Index`), and C# 14 extension members.

## Docker
```bash
# Build the image (SDK stage builds, ASP.NET runtime stage runs)
docker build -t taskapi:latest .

# Run it
docker run --rm -p 8080:8080 taskapi:latest

# Or with compose (also sets Development so /openapi/v1.json works)
docker compose up --build
```
Then set `@host = http://localhost:8080` in `TaskApi.http`.

Notes:
- Multi-stage build: the final image has only the ASP.NET runtime and your published app, not the SDK.
- Runs as a non-root user and listens on port 8080 (the default for .NET 8+ container images).
- Without `ASPNETCORE_ENVIRONMENT=Development` the image runs in Production mode, so the OpenAPI endpoint is off.
- Data is in memory, so it resets whenever the container restarts.

### Pulling the latest runtime with compose
`docker-compose.yml` sets `build.pull: true`, so `docker compose up --build` always pulls fresh
`dotnet/sdk:10.0` and `dotnet/aspnet:10.0` (runtime) images first. To pull them manually:
```bash
docker pull mcr.microsoft.com/dotnet/aspnet:10.0
docker pull mcr.microsoft.com/dotnet/sdk:10.0
```

## Persistence: Dapper + SQLite
- `Data.cs`: connection factory, `DateOnly` type handler, schema initializer.
- `TaskService.cs`: hand-written SQL, parameterized, with optimistic concurrency via the `Version` column.
- PATCH requires `"version"`; a stale version returns **409 Conflict**.
- Data is stored in `/data/tasks.db` inside the container, on the `taskdata` Docker volume.
  `docker compose down` keeps the data; `docker compose down -v` deletes it.

## Integration tests
Tests live in the sibling folder `TaskApi.Tests` (NOT inside `TaskApi`, or the web project would try to compile them).
```bash
dotnet test          # from the folder containing TaskApi.slnx
```
