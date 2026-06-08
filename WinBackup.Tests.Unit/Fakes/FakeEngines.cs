using WinBackup.Core.Backup;
using WinBackup.Core.State;

namespace WinBackup.Tests.Unit.Fakes;

public sealed class FakeProtonEngine : IProtonBackupEngine
{
    public int Calls { get; private set; }
    public Action? OnRun { get; set; }
    public TaskCompletionSource<bool>? Gate { get; set; }
    public bool Throw { get; set; }

    public async Task<BackupRecord> RunAsync(IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
    {
        Calls++;
        OnRun?.Invoke();
        if (Gate is not null)
        {
            await Gate.Task.ConfigureAwait(false);
        }

        if (Throw)
        {
            throw new InvalidOperationException("boom");
        }

        return new BackupRecord { Target = BackupTarget.Proton, ResultCode = BackupResultCode.Success };
    }
}

public sealed class FakeSsdEngine : ISsdBackupEngine
{
    public int Calls { get; private set; }
    public string? LastDestinationRoot { get; private set; }
    public Action? OnRun { get; set; }

    public Task<BackupRecord> RunAsync(string destinationRoot, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
    {
        Calls++;
        LastDestinationRoot = destinationRoot;
        OnRun?.Invoke();
        return Task.FromResult(new BackupRecord { Target = BackupTarget.Ssd, ResultCode = BackupResultCode.Success });
    }
}
