using System.Data;
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace TaskApi;

// ---- 1. Connection factory: a fresh connection per operation (never share one across requests) ----
public interface IDbConnectionFactory
{
    Task<SqliteConnection> OpenAsync();
}

public sealed class SqliteConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(connectionString);   // cheap: ADO.NET pools underneath
        await connection.OpenAsync();
        return connection;
    }
}

// ---- 2. Dapper doesn't know DateOnly, so teach it: stored as TEXT "yyyy-MM-dd" ----
public sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    private const string Format = "yyyy-MM-dd";

    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = value.ToString(Format, CultureInfo.InvariantCulture);
    }

    public override DateOnly Parse(object value) =>
        DateOnly.ParseExact((string)value, Format, CultureInfo.InvariantCulture);
}

// ---- 3. No migrations in Dapper: create the schema ourselves at startup ----
public sealed class DatabaseInitializer(IDbConnectionFactory connections)
{
    public async Task InitializeAsync()
    {
        await using var connection = await connections.OpenAsync();

        // WAL lets readers keep reading while one writer writes (good for a web API).
        await connection.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL;");

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS Tasks (
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                Title      TEXT    NOT NULL,
                Priority   INTEGER NOT NULL,
                IsDone     INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT    NOT NULL,
                DueDate    TEXT    NOT NULL,
                Version    INTEGER NOT NULL DEFAULT 1
            );
            """);   // raw string literal: no escaping needed
    }
}