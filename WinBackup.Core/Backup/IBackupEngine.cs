using WinBackup.Core.State;

namespace WinBackup.Core.Backup;

/// <summary>Daily Proton incremental backup.</summary>
public interface IProtonBackupEngine
{
    Task<BackupRecord> RunAsync(IProgress<BackupProgress>? progress = null, CancellationToken ct = default);
}

/// <summary>Monthly SSD full/incremental backup into a resolved destination root.</summary>
public interface ISsdBackupEngine
{
    Task<BackupRecord> RunAsync(string destinationRoot, IProgress<BackupProgress>? progress = null, CancellationToken ct = default);
}
