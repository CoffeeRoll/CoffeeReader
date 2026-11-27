using System.Threading.Channels;
using CoffeeReader.Application.Abstractions;
using CoffeeReader.Application.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeeReader.Infrastructure.Library;

/// <summary>
/// Runs library scans in the background: at startup, on a timer, when files change, and on request.
/// Requests that arrive while a scan is queued are merged into it.
/// </summary>
public sealed class LibraryScanService(
    IServiceScopeFactory scopeFactory,
    IOptions<LibraryScanOptions> scanOptions,
    IOptions<StorageOptions> storageOptions,
    ILogger<LibraryScanService> logger) : BackgroundService, ILibraryScanTrigger
{
    private readonly Channel<ScanRequest> _requests = Channel.CreateUnbounded<ScanRequest>(new UnboundedChannelOptions { SingleReader = true });
    private FileSystemWatcher? _watcher;

    public void RequestScan(string reason) => _requests.Writer.TryWrite(new ScanRequest(reason, Debounce: false));

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = scanOptions.Value;
        if (options.ScanOnStartup)
        {
            RequestScan("startup");
        }

        if (options.WatchForChanges)
        {
            StartWatching();
        }

        using var timer = options.IntervalMinutes > 0 ? new PeriodicTimer(TimeSpan.FromMinutes(options.IntervalMinutes)) : null;
        var timerLoop = timer is null ? Task.CompletedTask : RunTimerAsync(timer, stoppingToken);

        try
        {
            while (await _requests.Reader.WaitToReadAsync(stoppingToken))
            {
                var reasons = new List<string>();
                var debounce = false;
                while (_requests.Reader.TryRead(out var request))
                {
                    reasons.Add(request.Reason);
                    debounce |= request.Debounce;
                }

                if (debounce)
                {
                    // Let a burst of file changes (e.g. copying a folder of books) finish first.
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.DebounceSeconds)), stoppingToken);
                    while (_requests.Reader.TryRead(out var request))
                    {
                        reasons.Add(request.Reason);
                    }
                }

                await ScanAsync(string.Join(", ", reasons.Distinct()), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        await timerLoop;
    }

    private async Task ScanAsync(string reason, CancellationToken stoppingToken)
    {
        logger.LogInformation("Scanning library ({Reason})", reason);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var importer = scope.ServiceProvider.GetRequiredService<LibraryImporter>();
            await importer.ScanAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Already recorded in the scan state; keep the service alive for the next attempt.
            logger.LogDebug(ex, "Scan failed");
        }
    }

    private async Task RunTimerAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _requests.Writer.TryWrite(new ScanRequest("scheduled", Debounce: false));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StartWatching()
    {
        var root = Path.GetFullPath(storageOptions.Value.LibraryPath);
        if (!Directory.Exists(root))
        {
            logger.LogWarning("The library folder {Path} doesn't exist, so changes won't be detected", root);
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Created += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnChanged;
            _watcher.Error += (_, e) =>
            {
                logger.LogWarning(e.GetException(), "File watching hit an error; scanning to catch up");
                _requests.Writer.TryWrite(new ScanRequest("watcher overflow", Debounce: true));
            };
            _watcher.EnableRaisingEvents = true;
            logger.LogInformation("Watching {Path} for new books", root);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
        {
            logger.LogWarning(ex, "Couldn't watch {Path}; relying on periodic scans", root);
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        var extension = Path.GetExtension(e.FullPath);
        var isDirectory = extension.Length == 0;
        if (isDirectory || extension.Equals(".epub", StringComparison.OrdinalIgnoreCase))
        {
            _requests.Writer.TryWrite(new ScanRequest("files changed", Debounce: true));
        }
    }

    private sealed record ScanRequest(string Reason, bool Debounce);
}
