namespace CoffeeReader.Application.Library;

/// <summary>
/// Process-wide record of the most recent library scan.
/// </summary>
public sealed class LibraryScanState
{
    private readonly object _gate = new();
    private IReadOnlyList<ScanIssue> _issues = [];

    public bool IsScanning { get; private set; }

    public DateTime? LastStartedAt { get; private set; }

    public DateTime? LastCompletedAt { get; private set; }

    public ScanSummary? LastSummary { get; private set; }

    public string? LastError { get; private set; }

    public IReadOnlyList<ScanIssue> Issues
    {
        get
        {
            lock (_gate)
            {
                return _issues;
            }
        }
    }

    internal void Begin(DateTime when)
    {
        lock (_gate)
        {
            IsScanning = true;
            LastStartedAt = when;
        }
    }

    internal void Complete(DateTime when, ScanSummary summary, IReadOnlyList<ScanIssue> issues)
    {
        lock (_gate)
        {
            IsScanning = false;
            LastCompletedAt = when;
            LastSummary = summary;
            LastError = null;
            _issues = issues;
        }
    }

    internal void Fail(DateTime when, string error)
    {
        lock (_gate)
        {
            IsScanning = false;
            LastCompletedAt = when;
            LastError = error;
        }
    }
}

/// <summary>
/// Serialises imports so scans and uploads never race each other.
/// </summary>
public sealed class LibraryLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
