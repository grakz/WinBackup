using System.Globalization;

namespace WinBackup.Core.Backup;

/// <summary>
/// Pure schedule arithmetic: decides whether a backup is due "now" given the configured time and
/// the last run. Kept side-effect-free so it can be exhaustively unit-tested with a mock clock.
/// </summary>
public static class BackupSchedule
{
    /// <summary>Parses an "HH:MM" 24-hour string; falls back to midnight on malformed input.</summary>
    public static TimeSpan ParseTime(string hhmm)
    {
        if (TimeSpan.TryParseExact(hhmm, @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan t))
        {
            return t;
        }

        return TimeSpan.Zero;
    }

    /// <summary>
    /// Proton runs once per day. Due when the time of day has reached <paramref name="protonTime"/>
    /// and no Proton backup has run yet today.
    /// </summary>
    public static bool IsProtonDue(DateTimeOffset now, TimeSpan protonTime, DateTimeOffset? lastProtonRun)
    {
        if (now.TimeOfDay < protonTime)
        {
            return false;
        }

        return lastProtonRun is null || lastProtonRun.Value.LocalDateTime.Date < now.LocalDateTime.Date;
    }

    /// <summary>
    /// SSD runs once per month. Due once "now" has reached this month's scheduled moment
    /// (<paramref name="dayOfMonth"/> at <paramref name="ssdTime"/>) and no SSD backup has run yet
    /// this month. Because the trigger is "at or after" the scheduled moment, a machine that was off
    /// on the exact day still catches up later in the month.
    /// </summary>
    public static bool IsSsdDue(DateTimeOffset now, int dayOfMonth, TimeSpan ssdTime, DateTimeOffset? lastSsdRun)
    {
        int day = Math.Clamp(dayOfMonth, 1, DateTime.DaysInMonth(now.Year, now.Month));
        var dueMoment = new DateTimeOffset(now.Year, now.Month, day, 0, 0, 0, now.Offset).Add(ssdTime);

        if (now < dueMoment)
        {
            return false;
        }

        if (lastSsdRun is null)
        {
            return true;
        }

        DateTimeOffset last = lastSsdRun.Value;
        return last.Year < now.Year || (last.Year == now.Year && last.Month < now.Month);
    }
}
