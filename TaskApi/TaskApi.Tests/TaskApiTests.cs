using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskApi.Tests;

// IClassFixture = one factory (one app + one temp DB) shared by all tests in this class.
// Each test creates its own tasks, so tests don't depend on each other.
public class TaskApiTests : IClassFixture<TaskApiFactory>
{
    private readonly TaskApiFactory _factory;
    private readonly HttpClient _client;

    public TaskApiTests(TaskApiFactory factory)
    {
        _factory = factory;

        // Runs before EVERY test: if the app isn't on the temp DB, nothing gets written.
        factory.AssertIsolatedDatabase();
        _client = factory.CreateClient();
    }

    // Same JSON rules as the API: camelCase + enums as strings
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // ---------- helpers ----------
    private async Task<TaskItem> CreateAsync(string title = "test task")
    {
        var response = await _client.PostAsJsonAsync("/api/tasks", new { title, priority = "High" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskItem>(Json))!;
    }

    private Task<HttpResponseMessage> PatchAsync(int id, object body) =>
        _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/tasks/{id}")
        {
            Content = JsonContent.Create(body)
        });

    // ---------- safety ----------
    [Fact]
    public void App_UsesIsolatedTempDatabase_NotTheRealOne()
    {
        _factory.AssertIsolatedDatabase();

        // Proof the app's schema initializer ran against the temp file
        Assert.True(File.Exists(_factory.DbPath));
        Assert.StartsWith(Path.GetTempPath(), _factory.DbPath);
    }

    // ---------- POST ----------
    [Fact]
    public async Task Create_ReturnsCreated_WithLocationVersion1_AndDueDatePlus7()
    {
        var response = await _client.PostAsJsonAsync("/api/tasks", new { title = "  write tests  ", priority = "High" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var task = (await response.Content.ReadFromJsonAsync<TaskItem>(Json))!;
        Assert.Equal($"/api/tasks/{task.Id}", response.Headers.Location?.ToString());
        Assert.Equal("write tests", task.Title);                 // trimmed
        Assert.Equal(Priority.High, task.Priority);
        Assert.False(task.IsDone);
        Assert.Equal(1, task.Version);
        // Computed from the server's own timestamp, so it can't flake at midnight
        Assert.Equal(DateOnly.FromDateTime(task.CreatedUtc).AddDays(7), task.DueDate);
    }

    [Fact]
    public async Task Create_WithExplicitDueDate_KeepsIt()
    {
        var response = await _client.PostAsJsonAsync("/api/tasks", new { title = "x", dueDate = "2030-01-15" });
        var task = (await response.Content.ReadFromJsonAsync<TaskItem>(Json))!;

        Assert.Equal(new DateOnly(2030, 1, 15), task.DueDate);
    }

    [Fact]
    public async Task Create_WithBlankTitle_Returns400_WithTitleError()
    {
        var response = await _client.PostAsJsonAsync("/api/tasks", new { title = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("title", out _));
    }

    // ---------- GET ----------
    [Fact]
    public async Task Get_ExistingTask_ReturnsIt_AndUnknownId_Returns404()
    {
        var created = await CreateAsync("fetch me");

        var found = await _client.GetFromJsonAsync<TaskItem>($"/api/tasks/{created.Id}", Json);
        Assert.Equal(created, found);                            // records compare by value

        var missing = await _client.GetAsync("/api/tasks/999999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task List_FilteredByDone_ReturnsOnlyDoneTasks()
    {
        var open = await CreateAsync("still open");
        var done = await CreateAsync("finished");
        (await PatchAsync(done.Id, new { isDone = true, version = done.Version })).EnsureSuccessStatusCode();

        var doneList = await _client.GetFromJsonAsync<List<TaskItem>>("/api/tasks?done=true", Json);

        Assert.NotNull(doneList);
        Assert.All(doneList, t => Assert.True(t.IsDone));
        Assert.Contains(doneList, t => t.Id == done.Id);
        Assert.DoesNotContain(doneList, t => t.Id == open.Id);
    }

    // ---------- PATCH: optimistic concurrency ----------
    [Fact]
    public async Task Patch_WithCurrentVersion_Updates_AndBumpsVersion()
    {
        var task = await CreateAsync();

        var response = await PatchAsync(task.Id, new { isDone = true, version = task.Version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<TaskItem>(Json))!;
        Assert.True(updated.IsDone);
        Assert.Equal(task.Version + 1, updated.Version);
        Assert.Equal(task.Title, updated.Title);                 // untouched fields preserved
    }

    [Fact]
    public async Task Patch_WithStaleVersion_Returns409_AndDoesNotOverwrite()
    {
        var task = await CreateAsync("original");

        (await PatchAsync(task.Id, new { title = "first writer", version = 1 })).EnsureSuccessStatusCode();
        var stale = await PatchAsync(task.Id, new { title = "second writer", version = 1 });   // stale!

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var current = await _client.GetFromJsonAsync<TaskItem>($"/api/tasks/{task.Id}", Json);
        Assert.Equal("first writer", current!.Title);            // the stale write was rejected
        Assert.Equal(2, current.Version);
    }

    [Fact]
    public async Task Patch_WithoutVersion_Returns400()
    {
        var task = await CreateAsync();

        var response = await PatchAsync(task.Id, new { isDone = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_UnknownTask_Returns404()
    {
        var response = await PatchAsync(999999, new { isDone = true, version = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_Concurrent_ExactlyOneWriterWins()
    {
        var task = await CreateAsync("race");
        const int writers = 10;

        // All writers wait at the same gate, then fire at once with the SAME version
        var gate = new TaskCompletionSource();
        var requests = Enumerable.Range(0, writers).Select(async i =>
        {
            await gate.Task;
            return await PatchAsync(task.Id, new { title = $"writer {i}", version = task.Version });
        }).ToList();

        gate.SetResult();
        var responses = await Task.WhenAll(requests);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(writers - 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var final = await _client.GetFromJsonAsync<TaskItem>($"/api/tasks/{task.Id}", Json);
        Assert.Equal(task.Version + 1, final!.Version);          // bumped exactly once
    }

    // ---------- DELETE ----------
    [Fact]
    public async Task Delete_RemovesTask_ThenReturns404()
    {
        var task = await CreateAsync("delete me");

        var first = await _client.DeleteAsync($"/api/tasks/{task.Id}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var get = await _client.GetAsync($"/api/tasks/{task.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        var second = await _client.DeleteAsync($"/api/tasks/{task.Id}");
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }
}
