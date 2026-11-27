using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeeReader.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>Applies pending migrations and tunes SQLite for a small, concurrent web workload.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CoffeeReader.Database");

        var storage = scope.ServiceProvider.GetRequiredService<IOptions<StorageOptions>>().Value;
        EnsureWritable(storage.DataPath, required: true, logger);
        EnsureWritable(storage.LibraryPath, required: false, logger);

        await db.Database.MigrateAsync(cancellationToken);
        if (db.Database.IsSqlite())
        {
            // Write-ahead logging lets readers keep reading while a scan writes.
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        }

        logger.LogInformation("Database ready");
    }

    /// <summary>Fails fast with a helpful message when a volume isn't writable by the container user.</summary>
    private static void EnsureWritable(string path, bool required, ILogger logger)
    {
        var full = Path.GetFullPath(path);
        try
        {
            Directory.CreateDirectory(full);
            var probe = Path.Combine(full, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            var user = Environment.UserName;
            if (required)
            {
                logger.LogCritical("CoffeeReader can't write to {Path} (running as '{User}'). Make the data volume writable by this user, e.g. set PUID/PGID in docker-compose to the folder's owner.", full, user);
                throw new InvalidOperationException($"The data folder {full} isn't writable.", ex);
            }

            logger.LogWarning("The library folder {Path} is read-only for '{User}'. Reading works; uploads and deleting books won't.", full, user);
        }
    }
}
