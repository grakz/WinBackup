using WinBackup.Core.Backup;
using Xunit;

namespace WinBackup.Tests.Unit;

public sealed class BackupScheduleTests
{
    private static DateTimeOffset At(int y, int m, int d, int h, int min) =>
        new(y, m, d, h, min, 0, TimeSpan.Zero);

    [Fact]
    public void ParseTime_ValidAndInvalid()
    {
        Assert.Equal(new TimeSpan(18, 30, 0), BackupSchedule.ParseTime("18:30"));
        Assert.Equal(TimeSpan.Zero, BackupSchedule.ParseTime("garbage"));
    }

    [Fact]
    public void Proton_BeforeTime_NotDue()
    {
        Assert.False(BackupSchedule.IsProtonDue(At(2026, 5, 10, 1, 0), new TimeSpan(2, 0, 0), lastProtonRun: null));
    }

    [Fact]
    public void Proton_AfterTime_NeverRun_Due()
    {
        Assert.True(BackupSchedule.IsProtonDue(At(2026, 5, 10, 3, 0), new TimeSpan(2, 0, 0), lastProtonRun: null));
    }

    [Fact]
    public void Proton_AlreadyRanToday_NotDue()
    {
        var now = At(2026, 5, 10, 3, 0);
        Assert.False(BackupSchedule.IsProtonDue(now, new TimeSpan(2, 0, 0), lastProtonRun: At(2026, 5, 10, 2, 5)));
    }

    [Fact]
    public void Proton_RanYesterday_DueAgain()
    {
        var now = At(2026, 5, 10, 3, 0);
        Assert.True(BackupSchedule.IsProtonDue(now, new TimeSpan(2, 0, 0), lastProtonRun: At(2026, 5, 9, 2, 5)));
    }

    [Fact]
    public void Ssd_BeforeDay_NotDue()
    {
        Assert.False(BackupSchedule.IsSsdDue(At(2026, 5, 3, 20, 0), dayOfMonth: 5, new TimeSpan(18, 0, 0), lastSsdRun: null));
    }

    [Fact]
    public void Ssd_OnDayBeforeTime_NotDue()
    {
        Assert.False(BackupSchedule.IsSsdDue(At(2026, 5, 5, 17, 0), dayOfMonth: 5, new TimeSpan(18, 0, 0), lastSsdRun: null));
    }

    [Fact]
    public void Ssd_OnDayAfterTime_NeverRun_Due()
    {
        Assert.True(BackupSchedule.IsSsdDue(At(2026, 5, 5, 18, 30), dayOfMonth: 5, new TimeSpan(18, 0, 0), lastSsdRun: null));
    }

    [Fact]
    public void Ssd_AlreadyRanThisMonth_NotDue()
    {
        var now = At(2026, 5, 6, 18, 30);
        Assert.False(BackupSchedule.IsSsdDue(now, dayOfMonth: 5, new TimeSpan(18, 0, 0), lastSsdRun: At(2026, 5, 5, 18, 5)));
    }

    [Fact]
    public void Ssd_RanLastMonth_DueAgain()
    {
        var now = At(2026, 5, 6, 18, 30);
        Assert.True(BackupSchedule.IsSsdDue(now, dayOfMonth: 5, new TimeSpan(18, 0, 0), lastSsdRun: At(2026, 4, 5, 18, 5)));
    }

    [Fact]
    public void Ssd_MissedExactDay_StillDueLaterInMonth()
    {
        // Machine was off on the 5th; on the 9th it should still trigger this month's backup.
        var now = At(2026, 5, 9, 12, 0);
        Assert.True(BackupSchedule.IsSsdDue(now, dayOfMonth: 5, new TimeSpan(18, 0, 0), lastSsdRun: At(2026, 4, 5, 18, 5)));
    }
}
