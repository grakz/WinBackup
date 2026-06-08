using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WinBackup.Core.Abstractions;
using WinBackup.Core.Config;
using WinBackup.Core.State;

namespace WinBackup.Core.Backup;

public enum OrchestratorStatus
{
    Idle,
    RunningProton,
    RunningSsd,
    Error,
}

/// <summary>
/// Coordinates scheduled and manual backups. Guarantees a single backup runs at a time (a second
/// trigger is silently skipped), tracks <see cref="Status"/> for the UI, and forwards per-file
/// <see cref="Progress"/>. The actual cadence is driven by calling <see cref="TickAsync"/> from a
/// timer; the due decision is delegated to <see cref="BackupSchedule"/> so it is fully testable.
/// </summary>
public sealed class BackupOrchestrator
{
    private readonly IProtonBackupEngine _proton;
    private readonly ISsdBackupEngine _ssd;
    private readonly Func<CancellationToken, Task<string?>> _resolveSsdRoot;
    private readonly StateService _state;
    private readonly string _statePath;
    private readonly IClock _clock;
    private readonly BackupConfig _config;
    private readonly ILogger _log;

    private int _busy; // 0 = idle, 1 = a backup is running

    public BackupOrchestrator(
        IProtonBackupEngine proton,
        ISsdBackupEngine ssd,
        Func<CancellationToken, Task<string?>> resolveSsdRoot,
        StateService state,
        string statePath,
        IClock clock,
        BackupConfig config,
        ILogger? log = null)
    {
        _proton = proton;
        _ssd = ssd;
        _resolveSsdRoot = resolveSsdRoot;
        _state = state;
        _statePath = statePath;
        _clock = clock;
        _config = config;
        _log = log ?? NullLogger.Instance;
    }

    public OrchestratorStatus Status { get; private set; } = OrchestratorStatus.Idle;

    /// <summary>Raised for every per-file progress update from a running backup.</summary>
    public event Action<BackupProgress>? Progress;

    /// <summary>True while a backup is in progress (used to gate manual triggers from the UI).</summary>
    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    /// <summary>
    /// Evaluates the schedule against the current time and runs whatever is due. SSD takes priority
    /// over Proton when both are due. Returns the target that ran, or <c>null</c> if nothing was due
    /// or a backup was already running.
    /// </summary>
    public async Task<BackupTarget?> TickAsync(CancellationToken ct = default)
    {
        DateTimeOffset now = _clock.Now;
        BackupState state = _state.Load(_statePath);

        DateTimeOffset? lastSsd = _state.GetLastSuccessful(state, BackupTarget.Ssd)?.StartedAt;
        DateTimeOffset? lastProton = _state.GetLastSuccessful(state, BackupTarget.Proton)?.StartedAt;

        if (BackupSchedule.IsSsdDue(now, _config.Schedule.SsdDayOfMonth, BackupSchedule.ParseTime(_config.Schedule.SsdTime), lastSsd))
        {
            return await RunSsdAsync(ct).ConfigureAwait(false) ? BackupTarget.Ssd : null;
        }

        if (BackupSchedule.IsProtonDue(now, BackupSchedule.ParseTime(_config.Schedule.ProtonTime), lastProton))
        {
            return await RunProtonAsync(ct).ConfigureAwait(false) ? BackupTarget.Proton : null;
        }

        return null;
    }

    /// <summary>Manually runs a Proton backup. Returns false if one was skipped because a backup is already running.</summary>
    public Task<bool> RunProtonAsync(CancellationToken ct = default) =>
        RunGuardedAsync(OrchestratorStatus.RunningProton, async progress =>
        {
            await _proton.RunAsync(progress, ct).ConfigureAwait(false);
        }, ct);

    /// <summary>Manually runs an SSD backup. Returns false if skipped (already running) or the SSD is not connected.</summary>
    public Task<bool> RunSsdAsync(CancellationToken ct = default) =>
        RunGuardedAsync(OrchestratorStatus.RunningSsd, async progress =>
        {
            string? root = await _resolveSsdRoot(ct).ConfigureAwait(false);
            if (root is null)
            {
                _log.LogWarning("SSD backup skipped: backup SSD not connected.");
                throw new SsdNotConnectedException();
            }

            await _ssd.RunAsync(root, progress, ct).ConfigureAwait(false);
        }, ct);

    private async Task<bool> RunGuardedAsync(
        OrchestratorStatus runningStatus,
        Func<IProgress<BackupProgress>, Task> action,
        CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            _log.LogInformation("Backup trigger skipped: a backup is already running.");
            return false;
        }

        var progress = new Progress<BackupProgress>(p => Progress?.Invoke(p));
        try
        {
            Status = runningStatus;
            await action(progress).ConfigureAwait(false);
            Status = OrchestratorStatus.Idle;
            return true;
        }
        catch (OperationCanceledException)
        {
            Status = OrchestratorStatus.Idle;
            throw;
        }
        catch (SsdNotConnectedException)
        {
            Status = OrchestratorStatus.Idle;
            return false;
        }
        catch (Exception ex)
        {
            Status = OrchestratorStatus.Error;
            _log.LogError(ex, "Backup failed.");
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }
}

/// <summary>Signals that an SSD backup could not run because the backup SSD is not connected.</summary>
public sealed class SsdNotConnectedException : Exception
{
    public SsdNotConnectedException() : base("The backup SSD is not connected.") { }
}
