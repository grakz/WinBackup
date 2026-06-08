using WinBackup.Core.Abstractions;
using WinBackup.Core.Config;
using WinBackup.Core.State;

namespace WinBackup.Core.Backup;

/// <summary>
/// Monthly SSD backup. The first successful backup of a calendar year is a full copy into
/// <c>{root}\{YYYY}_FULL</c>; later runs that year are incrementals into <c>{root}\{YYYY-MM}_INCR</c>
/// containing only files modified since the last SSD backup. A single failed file never aborts the run.
/// </summary>
public sealed class SsdBackupEngine : ISsdBackupEngine
{
    private readonly IFileSystem _fs;
    private readonly FileSetCopier _copier;
    private readonly StateService _state;
    private readonly string _statePath;
    private readonly IClock _clock;
    private readonly BackupConfig _config;

    public SsdBackupEngine(
        IFileSystem fs,
        FileSetCopier copier,
        StateService state,
        string statePath,
        IClock clock,
        BackupConfig config)
    {
        _fs = fs;
        _copier = copier;
        _state = state;
        _statePath = statePath;
        _clock = clock;
        _config = config;
    }

    /// <summary>Runs the SSD backup, writing into <paramref name="destinationRoot"/> (the resolved SSD backup folder).</summary>
    public async Task<BackupRecord> RunAsync(
        string destinationRoot,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default)
    {
        DateTimeOffset started = _clock.Now;
        int year = started.Year;
        BackupState state = _state.Load(_statePath);

        bool hasFullThisYear = state.Records.Any(r =>
            r.Target == BackupTarget.Ssd
            && r.Kind == BackupKind.Full
            && (r.ResultCode is BackupResultCode.Success or BackupResultCode.PartialSuccess)
            && r.StartedAt.Year == year);

        BackupKind kind = hasFullThisYear ? BackupKind.Incremental : BackupKind.Full;

        string targetFolder;
        DateTimeOffset? cutoff;
        string phase;
        if (kind == BackupKind.Full)
        {
            targetFolder = Path.Combine(destinationRoot, $"{year}_FULL");
            cutoff = null;
            phase = "SSD full backup";
        }
        else
        {
            targetFolder = Path.Combine(destinationRoot, $"{year}-{started.Month:00}_INCR");
            cutoff = _state.GetLastSuccessful(state, BackupTarget.Ssd)?.StartedAt;
            phase = "SSD incremental backup";
        }

        IReadOnlyList<CopyJob> jobs = BackupJobBuilder.Build(_fs, _config.SourceFolders, targetFolder, cutoff);
        CopyTally tally = await _copier.CopyAllAsync(jobs, phase, progress, ct).ConfigureAwait(false);

        var record = new BackupRecord
        {
            Target = BackupTarget.Ssd,
            Kind = kind,
            StartedAt = started,
            CompletedAt = _clock.Now,
            ResultCode = tally.ToResultCode(),
            FilesCopiedCount = tally.Copied,
            VssFallbackCount = tally.Vss,
            ExcludedCount = tally.Excluded,
            SkippedCount = tally.Skipped,
            ErrorMessage = tally.Errors.Count == 0 ? null : string.Join("; ", tally.Errors.Take(20)),
        };

        _state.AddRecord(_statePath, record);
        return record;
    }
}
