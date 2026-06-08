using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WinBackup.Core.Abstractions;
using WinBackup.Core.OneDrive;
using WinBackup.Core.State;

namespace WinBackup.Core.Backup;

/// <summary>A source file paired with its computed destination path.</summary>
public sealed record CopyJob(FileItem Source, string Destination);

/// <summary>Running tally of a backup's file outcomes, used to build the final <see cref="BackupRecord"/>.</summary>
public sealed class CopyTally
{
    public int Copied { get; set; }
    public int Vss { get; set; }
    public int Excluded { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; } = new();

    public BackupResultCode ToResultCode()
    {
        if (Skipped > 0 || Errors.Count > 0)
        {
            // Some files failed but the run still produced a backup → partial.
            return Copied > 0 || Vss > 0 ? BackupResultCode.PartialSuccess : BackupResultCode.Failed;
        }

        return BackupResultCode.Success;
    }
}

/// <summary>
/// Copies a set of <see cref="CopyJob"/>s using <see cref="FileCopyService"/>, routing locked files
/// to an optional <see cref="ILockedFileHandler"/> and never aborting the whole run on a single
/// file failure. Shared by the SSD and Proton engines.
/// </summary>
public sealed class FileSetCopier
{
    private readonly FileCopyService _copy;
    private readonly ILockedFileHandler? _lockedHandler;
    private readonly IPlaceholderController _placeholders;
    private readonly ILogger _log;

    public FileSetCopier(
        FileCopyService copy,
        ILockedFileHandler? lockedHandler = null,
        IPlaceholderController? placeholders = null,
        ILogger? log = null)
    {
        _copy = copy ?? throw new ArgumentNullException(nameof(copy));
        _lockedHandler = lockedHandler;
        _placeholders = placeholders ?? NullPlaceholderController.Instance;
        _log = log ?? NullLogger.Instance;
    }

    public async Task<CopyTally> CopyAllAsync(
        IReadOnlyList<CopyJob> jobs,
        string phase,
        IProgress<BackupProgress>? progress,
        CancellationToken ct)
    {
        var tally = new CopyTally();
        for (int i = 0; i < jobs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            CopyJob job = jobs[i];

            var fileProgress = new Progress<FileCopyProgress>(
                fp => progress?.Report(new BackupProgress(phase, i, jobs.Count, fp)));

            try
            {
                FileCopyResult result = await _copy
                    .CopyAsync(job.Source.Path, job.Destination, fileProgress, ct)
                    .ConfigureAwait(false);

                switch (result.Outcome)
                {
                    case FileCopyOutcome.Copied:
                        tally.Copied++;
                        DehydrateIfCloudOnly(job, tally);
                        break;
                    case FileCopyOutcome.Excluded:
                        tally.Excluded++;
                        _log.LogDebug("Excluded by filter: {Path}", job.Source.Path);
                        break;
                    case FileCopyOutcome.RequiresVssFallback:
                        await HandleLockedAsync(job, fileProgress, tally, ct).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A single bad file (hash mismatch, persistent I/O error, etc.) must not abort the run.
                tally.Skipped++;
                tally.Errors.Add($"{job.Source.Path}: {ex.Message}");
                _log.LogWarning(ex, "Skipped file after error: {Path}", job.Source.Path);
            }

            progress?.Report(new BackupProgress(phase, i + 1, jobs.Count, null));
        }

        return tally;
    }

    private void DehydrateIfCloudOnly(CopyJob job, CopyTally tally)
    {
        if (!job.Source.IsCloudOnly)
        {
            return;
        }

        try
        {
            // The file was hydrated on read during copy; return it to cloud-only to reclaim disk.
            _placeholders.Dehydrate(job.Source.Path);
        }
        catch (Exception ex)
        {
            // Dehydration is best-effort: the backup itself succeeded, so mark partial, don't fail.
            tally.Errors.Add($"{job.Source.Path}: copied but dehydration failed ({ex.Message})");
            _log.LogWarning(ex, "Dehydration failed after backup: {Path}", job.Source.Path);
        }
    }

    private async Task HandleLockedAsync(
        CopyJob job,
        IProgress<FileCopyProgress> fileProgress,
        CopyTally tally,
        CancellationToken ct)
    {
        if (_lockedHandler is null)
        {
            // No VSS available (e.g. UAC declined or Phase 4 not wired): record as skipped.
            tally.Skipped++;
            tally.Errors.Add($"{job.Source.Path}: locked, VSS fallback unavailable");
            return;
        }

        bool ok = await _lockedHandler
            .TryCopyLockedAsync(job.Source.Path, job.Destination, fileProgress, ct)
            .ConfigureAwait(false);

        if (ok)
        {
            tally.Vss++;
        }
        else
        {
            tally.Skipped++;
            tally.Errors.Add($"{job.Source.Path}: locked, VSS fallback failed");
        }
    }
}
