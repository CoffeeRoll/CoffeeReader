namespace CoffeeReader.Application.Library;

public sealed record ScanSummary(int Added, int Updated, int Restored, int Missing, int Failed, double DurationSeconds);

public sealed record ScanIssue(string Path, string Message);

public sealed record LibraryStatusDto(
    bool IsScanning,
    DateTime? LastScanStartedAt,
    DateTime? LastScanCompletedAt,
    ScanSummary? LastScan,
    string? LastError,
    IReadOnlyList<ScanIssue> Issues,
    int BookCount,
    int MissingCount);

public enum UploadStatus
{
    Added,
    Duplicate,
    Failed,
}

public sealed record UploadResultDto(string FileName, UploadStatus Status, int? BookId, string? Title, string? Error);
