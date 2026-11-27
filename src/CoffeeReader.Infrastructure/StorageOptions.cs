namespace CoffeeReader.Infrastructure;

/// <summary>
/// Where CoffeeReader keeps things on disk. In the container these are the two mounted volumes.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>The books volume. Drop files here, or upload them through the web UI.</summary>
    public string LibraryPath { get; set; } = "/books";

    /// <summary>The app data volume: database, cover thumbnails and encryption keys.</summary>
    public string DataPath { get; set; } = "/config";

    /// <summary>Folder (inside <see cref="LibraryPath"/>) that uploads are written to.</summary>
    public string UploadFolder { get; set; } = "Uploads";

    /// <summary>Overrides the default SQLite database location.</summary>
    public string? ConnectionString { get; set; }
}

public sealed class LibraryScanOptions
{
    public const string SectionName = "LibraryScan";

    /// <summary>Scan when the app starts.</summary>
    public bool ScanOnStartup { get; set; } = true;

    /// <summary>React to files being added, moved or removed. Some network shares don't report changes.</summary>
    public bool WatchForChanges { get; set; } = true;

    /// <summary>Periodic full scan, as a fallback for file systems that don't report changes. 0 disables it.</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>How long to wait for a burst of file changes to settle before scanning.</summary>
    public int DebounceSeconds { get; set; } = 5;
}
