using System.Text.Json.Serialization;
using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using TaskApi;

var builder = WebApplication.CreateBuilder(args);

// Dapper: teach it about DateOnly (once, at startup)
SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());

// Connection string: appsettings / env var ConnectionStrings__Default, else a local file.
// Resolved lazily (inside the factory lambda) so tests can override configuration.
builder.Services.AddSingleton<IDbConnectionFactory>(sp =>
    new SqliteConnectionFactory(
        sp.GetRequiredService<IConfiguration>().GetConnectionString("Default") ?? "Data Source=tasks.db"));
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITaskService, TaskService>();

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

// Create the table if it doesn't exist (no migrations in Dapper)
await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/", () => "TaskApi is running. Try GET /api/tasks");

var tasks = app.MapGroup("/api/tasks");

tasks.MapGet("/", async (ITaskService service, bool? done) =>
    TypedResults.Ok(await service.GetAllAsync(done)));

tasks.MapGet("/{id:int}",
    async Task<Results<Ok<TaskItem>, NotFound>> (int id, ITaskService service) =>
        await service.GetAsync(id) is { } task
            ? TypedResults.Ok(task)
            : TypedResults.NotFound());

tasks.MapPost("/",
    async Task<Results<Created<TaskItem>, ValidationProblem>> (CreateTaskRequest request, ITaskService service) =>
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["Title is required."]
            });
        }

        var created = await service.CreateAsync(request);
        return TypedResults.Created($"/api/tasks/{created.Id}", created);
    });

tasks.MapPatch("/{id:int}",
    async Task<Results<Ok<TaskItem>, NotFound, Conflict<string>, ValidationProblem>>
        (int id, UpdateTaskRequest request, ITaskService service) =>
    {
        if (request.Version is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["version"] = ["Version is required. Send the version from your last GET."]
            });
        }

        var result = await service.UpdateAsync(id, request);

        return result.Outcome switch
        {
            UpdateOutcome.Updated  => TypedResults.Ok(result.Item!),
            UpdateOutcome.NotFound => TypedResults.NotFound(),
            _                      => TypedResults.Conflict("The task was modified by someone else. GET it again and retry.")
        };
    });

tasks.MapDelete("/{id:int}",
    async Task<Results<NoContent, NotFound>> (int id, ITaskService service) =>
        await service.DeleteAsync(id)
            ? TypedResults.NoContent()
            : TypedResults.NotFound());

app.Run();

// Makes the auto-generated Program class visible to the test project (WebApplicationFactory<Program>)
public partial class Program;
