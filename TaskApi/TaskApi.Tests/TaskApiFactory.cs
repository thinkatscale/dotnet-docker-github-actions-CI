using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TaskApi.Tests;

// Boots the REAL app in memory (real DI, real middleware, real Dapper, real SQLite),
// but pointed at a throwaway database file so tests never touch your real data.
public class TaskApiFactory : WebApplicationFactory<Program>
{
    private const string Marker = "taskapi-test-";

    public string DbPath { get; } =
        Path.Combine(Path.GetTempPath(), $"{Marker}{Guid.NewGuid():N}.db");

    private string ExpectedConnectionString => $"Data Source={DbPath}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Added at build time, AFTER the app's own sources, so it wins over appsettings.json
        // AND over a stray ConnectionStrings__Default environment variable on your machine.
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ExpectedConnectionString
            }));
    }

    // SAFETY GUARD: throws if the running app is not using this factory's temp database.
    public void AssertIsolatedDatabase()
    {
        var actual = Services.GetRequiredService<IConfiguration>().GetConnectionString("Default");

        if (actual != ExpectedConnectionString || !DbPath.Contains(Marker))
        {
            throw new InvalidOperationException(
                $"Tests are NOT using the temp database! Expected '{ExpectedConnectionString}' " +
                $"but the app is using '{actual}'. Aborting to protect real data.");
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        SqliteConnection.ClearAllPools();   // release file handles so the files can be deleted
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(DbPath + suffix); } catch (IOException) { /* best effort */ }
        }
    }
}
