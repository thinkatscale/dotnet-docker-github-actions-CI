using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace TaskApi;

public interface ITaskService
{
    Task<IReadOnlyList<TaskItem>> GetAllAsync(bool? done);
    Task<TaskItem?> GetAsync(int id);
    Task<TaskItem> CreateAsync(CreateTaskRequest request);
    Task<UpdateResult> UpdateAsync(int id, UpdateTaskRequest request);
    Task<bool> DeleteAsync(int id);
}

// Still a Singleton: it holds no connection, only a factory that makes one per call.
public class TaskService(IDbConnectionFactory connections, TimeProvider clock, ILogger<TaskService> logger)
    : ITaskService
{
    private const string Columns = "Id, Title, Priority, IsDone, CreatedUtc, DueDate, Version";

    // Raw shape of a row, with the types SQLite really stores (INTEGER -> long, TEXT -> string).
    // We query into this, then convert to the clean TaskItem record ourselves.
    private sealed class TaskRow
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public long Priority { get; set; }
        public long IsDone { get; set; }
        public string CreatedUtc { get; set; } = "";
        public DateOnly DueDate { get; set; }       // converted by DateOnlyTypeHandler
        public long Version { get; set; }
    }

    private static TaskItem ToItem(TaskRow r) => new(
        (int)r.Id,
        r.Title,
        (Priority)(int)r.Priority,
        r.IsDone != 0,
        DateTime.Parse(r.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        r.DueDate,
        (int)r.Version);

    // ---------- READ ----------
    public async Task<IReadOnlyList<TaskItem>> GetAllAsync(bool? done)
    {
        await using var connection = await connections.OpenAsync();

        // "@Done IS NULL OR ..." = optional filter in one query. Parameters are NEVER concatenated.
        var rows = await connection.QueryAsync<TaskRow>(
            $"SELECT {Columns} FROM Tasks WHERE (@Done IS NULL OR IsDone = @Done) ORDER BY Id",
            new { Done = done is null ? (int?)null : (done.Value ? 1 : 0) });

        return [.. rows.Select(ToItem)];
    }

    public async Task<TaskItem?> GetAsync(int id)
    {
        await using var connection = await connections.OpenAsync();
        return await GetByIdAsync(connection, id);
    }

    private static async Task<TaskItem?> GetByIdAsync(SqliteConnection connection, int id)
    {
        var row = await connection.QuerySingleOrDefaultAsync<TaskRow>(
            $"SELECT {Columns} FROM Tasks WHERE Id = @Id", new { Id = id });

        return row is null ? null : ToItem(row);
    }

    // ---------- CREATE ----------
    public async Task<TaskItem> CreateAsync(CreateTaskRequest request)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var dueDate = request.DueDate ?? DateOnly.FromDateTime(now).AddDays(7);
        var title = request.Title.Trim();

        await using var connection = await connections.OpenAsync();

        // The database generates the Id; RETURNING hands it back (SQLite 3.35+).
        var id = await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO Tasks (Title, Priority, IsDone, CreatedUtc, DueDate, Version)
            VALUES (@Title, @Priority, 0, @CreatedUtc, @DueDate, 1)
            RETURNING Id;
            """,
            new
            {
                Title = title,
                Priority = (int)request.Priority,
                CreatedUtc = now.ToString("O", CultureInfo.InvariantCulture),
                DueDate = dueDate
            });

        logger.LogInformation("Created task {TaskId}: {Title}", id, title);
        return new TaskItem((int)id, title, request.Priority, false, now, dueDate, 1);
    }

    // ---------- UPDATE (optimistic concurrency) ----------
    public async Task<UpdateResult> UpdateAsync(int id, UpdateTaskRequest request)
    {
        var expectedVersion = request.Version
            ?? throw new ArgumentException("Version is required.", nameof(request));

        await using var connection = await connections.OpenAsync();

        var existing = await GetByIdAsync(connection, id);
        if (existing is null)
            return new UpdateResult(UpdateOutcome.NotFound);

        // Dapper has no change tracking, so we build the new state ourselves...
        var updated = existing with
        {
            Title = request.Title?.Trim() ?? existing.Title,
            Priority = request.Priority ?? existing.Priority,
            IsDone = request.IsDone ?? existing.IsDone,
            DueDate = request.DueDate ?? existing.DueDate
        };

        // ...and write it explicitly. "AND Version = @Version" is the whole concurrency check:
        // it only matches if nobody changed the row since the version the CLIENT saw.
        var rowsAffected = await connection.ExecuteAsync(
            """
            UPDATE Tasks
            SET Title = @Title, Priority = @Priority, IsDone = @IsDone,
                DueDate = @DueDate, Version = Version + 1
            WHERE Id = @Id AND Version = @Version
            """,
            new
            {
                Id = id,
                updated.Title,
                Priority = (int)updated.Priority,
                IsDone = updated.IsDone ? 1 : 0,
                updated.DueDate,
                Version = expectedVersion
            });

        if (rowsAffected == 1)
            return new UpdateResult(UpdateOutcome.Updated, updated with { Version = expectedVersion + 1 });

        // 0 rows: either someone updated it (conflict) or deleted it just now (not found).
        var stillExists = await GetByIdAsync(connection, id) is not null;
        logger.LogWarning("Update of task {TaskId} rejected (stale version {Version})", id, expectedVersion);
        return new UpdateResult(stillExists ? UpdateOutcome.Conflict : UpdateOutcome.NotFound);
    }

    // ---------- DELETE ----------
    public async Task<bool> DeleteAsync(int id)
    {
        await using var connection = await connections.OpenAsync();
        var rowsAffected = await connection.ExecuteAsync("DELETE FROM Tasks WHERE Id = @Id", new { Id = id });
        return rowsAffected > 0;
    }
}