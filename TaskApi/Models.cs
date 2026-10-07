namespace TaskApi;

public enum Priority { Low, Medium, High }

// Version is the optimistic-concurrency token: it goes up by 1 on every successful update.
public record TaskItem(int Id, string Title, Priority Priority, bool IsDone,
                       DateTime CreatedUtc, DateOnly DueDate, int Version);

public record CreateTaskRequest(string Title, Priority Priority = Priority.Medium,
                                DateOnly? DueDate = null);

// Version = "the version I last saw". The server rejects the update if it is stale.
public record UpdateTaskRequest(string? Title, Priority? Priority, bool? IsDone,
                                DateOnly? DueDate, int? Version);

public enum UpdateOutcome { Updated, NotFound, Conflict }

public record UpdateResult(UpdateOutcome Outcome, TaskItem? Item = null);
