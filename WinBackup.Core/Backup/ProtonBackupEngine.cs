using WinBackup.Core.Abstractions;
using WinBackup.Core.Config;
using WinBackup.Core.State;

namespace WinBackup.Core.Backup;

/// <summary>
/// Daily Proton backup. Copies everything modified since the resilient cutoff (see
/// <see cref="CutoffCalculator"/>) into a dated <c>{syncFolder}\{YYYY-MM-DD}</c> folder that the
/// Proton desktop app uploads automatically. A run with no changed files creates no folder and is
/// not recorded, so empty days leave no clutter.
/// </summary>
public sealed class ProtonBackupEngine : IProtonBackupEngine
{
    private readonly IFileSystem _fs;
    private readonly FileSetCopier _copier;
    private readonly StateService _state;
    private readonly string _statePath;
    private readonly IClock _clock;
    private readonly BackupConfig _config;

    public ProtonBackupEngine(
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

    /// <summary>
    /// Runs the Proton backup. Returns the resulting record; when nothing changed, the returned record
    /// has <see cref="BackupRecord.FilesCopiedCount"/> 0 and is <em>not</em> persisted.
    /// </summary>
    public async Task<BackupRecord> RunAsync(
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default)
    {
        DateTimeOffset started = _clock.Now;
        BackupState state = _state.Load(_statePath);
        DateTimeOffset? cutoff = CutoffCalculator.GetProtonCutoff(state, _config.Proton.LookbackBackups);

        string targetFolder = Path.Combine(_config.Proton.SyncFolder, started.ToString("yyyy-MM-dd"));
        IReadOnlyList<CopyJob> jobs = BackupJobBuilder.Build(_fs, _config.SourceFolders, targetFolder, cutoff);

        if (jobs.Count == 0)
        {
            // Nothing changed since the cutoff — skip entirely (no folder, no record).
            return new BackupRecord
            {
                Target = BackupTarget.Proton,
                Kind = BackupKind.Delta,
                StartedAt = started,
                CompletedAt = _clock.Now,
                ResultCode = BackupResultCode.Success,
                FilesCopiedCount = 0,
            };
        }

        CopyTally tally = await _copier.CopyAllAsync(jobs, "Proton backup", progress, ct).ConfigureAwait(false);

        var record = new BackupRecord
        {
            Target = BackupTarget.Proton,
            Kind = BackupKind.Delta,
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
