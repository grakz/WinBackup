using WinBackup.Core.Backup;
using WinBackup.Core.State;
using Xunit;

namespace WinBackup.Tests.Unit;

public sealed class CutoffCalculatorTests
{
    private static BackupRecord Ssd(DateTimeOffset completedAt, BackupResultCode code = BackupResultCode.Success) => new()
    {
        Target = BackupTarget.Ssd,
        ResultCode = code,
        StartedAt = completedAt.AddMinutes(-5),
        CompletedAt = completedAt,
    };

    [Fact]
    public void NoSsdBackups_ReturnsNull()
    {
        Assert.Null(CutoffCalculator.GetProtonCutoff(new BackupState(), lookbackBackups: 2));
    }

    [Fact]
    public void OnlyFailedSsdBackups_ReturnsNull()
    {
        var state = new BackupState
        {
            Records = { Ssd(DateTimeOffset.UtcNow, BackupResultCode.Failed) },
        };

        Assert.Null(CutoffCalculator.GetProtonCutoff(state, lookbackBackups: 2));
    }

    [Fact]
    public void SingleSsdBackup_LookbackTwo_ReturnsThatBackup()
    {
        var completed = new DateTimeOffset(2026, 5, 1, 18, 0, 0, TimeSpan.Zero);
        var state = new BackupState { Records = { Ssd(completed) } };

        DateTimeOffset? cutoff = CutoffCalculator.GetProtonCutoff(state, lookbackBackups: 2);

        Assert.Equal(completed.AddMinutes(-5), cutoff);
    }

    [Fact]
    public void FiveSsdBackups_LookbackTwo_ReturnsSecondMostRecent()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 18, 0, 0, TimeSpan.Zero);
        var state = new BackupState
        {
            Records =
            {
                Ssd(baseTime.AddMonths(0)),
                Ssd(baseTime.AddMonths(1)),
                Ssd(baseTime.AddMonths(2)),
                Ssd(baseTime.AddMonths(3)),
                Ssd(baseTime.AddMonths(4)), // most recent
            },
        };

        DateTimeOffset? cutoff = CutoffCalculator.GetProtonCutoff(state, lookbackBackups: 2);

        // Second most recent is baseTime + 3 months; cutoff is its StartedAt.
        Assert.Equal(baseTime.AddMonths(3).AddMinutes(-5), cutoff);
    }

    [Fact]
    public void GapOfSeveralMonths_LookbackTwo_ReachesBackToOlderBackup()
    {
        // Two SSD backups months apart; lookback=2 must reach the older one so Proton
        // re-copies everything since then (no data-loss gap from the missed months).
        var jan = new DateTimeOffset(2026, 1, 1, 18, 0, 0, TimeSpan.Zero);
        var jun = new DateTimeOffset(2026, 6, 1, 18, 0, 0, TimeSpan.Zero);
        var state = new BackupState { Records = { Ssd(jan), Ssd(jun) } };

        DateTimeOffset? cutoff = CutoffCalculator.GetProtonCutoff(state, lookbackBackups: 2);

        Assert.Equal(jan.AddMinutes(-5), cutoff);
    }

    [Fact]
    public void LookbackBelowOne_TreatedAsOne_ReturnsMostRecent()
    {
        var older = new DateTimeOffset(2026, 1, 1, 18, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 2, 1, 18, 0, 0, TimeSpan.Zero);
        var state = new BackupState { Records = { Ssd(older), Ssd(newer) } };

        DateTimeOffset? cutoff = CutoffCalculator.GetProtonCutoff(state, lookbackBackups: 0);

        Assert.Equal(newer.AddMinutes(-5), cutoff);
    }
}
