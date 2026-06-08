using WinBackup.Core.State;

namespace WinBackup.Core.Backup;

/// <summary>
/// Computes the Proton incremental cutoff from backup history.
/// <para>
/// Proton copies everything modified since the cutoff. Using the Nth-most-recent successful
/// SSD backup (rather than the last Proton run) means a missed monthly SSD run never opens a
/// data-loss gap: Proton keeps reaching back until an SSD backup has safely captured the files.
/// </para>
/// </summary>
public static class CutoffCalculator
{
    /// <summary>
    /// Returns the cutoff timestamp: the <see cref="BackupRecord.StartedAt"/> of the
    /// <paramref name="lookbackBackups"/>-th most recent successful SSD backup. If fewer than
    /// that many successful SSD backups exist, the oldest available one is used. Returns
    /// <c>null</c> when no successful SSD backup exists (implying a full copy is needed).
    /// </summary>
    /// <param name="state">Backup history.</param>
    /// <param name="lookbackBackups">How many SSD backups to look back (1 = most recent). Values &lt; 1 are treated as 1.</param>
    public static DateTimeOffset? GetProtonCutoff(BackupState state, int lookbackBackups)
    {
        ArgumentNullException.ThrowIfNull(state);

        List<BackupRecord> ssdSuccesses = state.Records
            .Where(r => r.Target == BackupTarget.Ssd && IsSuccessful(r.ResultCode))
            .OrderByDescending(r => r.CompletedAt)
            .ToList();

        if (ssdSuccesses.Count == 0)
        {
            return null;
        }

        int lookback = Math.Max(1, lookbackBackups);
        int index = Math.Min(lookback, ssdSuccesses.Count) - 1;
        return ssdSuccesses[index].StartedAt;
    }

    private static bool IsSuccessful(BackupResultCode code) =>
        code is BackupResultCode.Success or BackupResultCode.PartialSuccess;
}
