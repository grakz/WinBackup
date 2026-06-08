namespace WinBackup.Core.Backup;

/// <summary>Coarse progress for a whole backup run, surfaced to the status UI.</summary>
public sealed record BackupProgress(
    string Phase,
    int FilesDone,
    int FilesTotal,
    FileCopyProgress? Current);

/// <summary>
/// Handles files that <see cref="FileCopyService"/> reported as locked
/// (<see cref="FileCopyOutcome.RequiresVssFallback"/>). The Phase 4 implementation copies from a
/// VSS snapshot via the elevated helper; when no handler is supplied, locked files are skipped.
/// </summary>
public interface ILockedFileHandler
{
    Task<bool> TryCopyLockedAsync(
        string source,
        string dest,
        IProgress<FileCopyProgress>? progress,
        CancellationToken ct);
}
