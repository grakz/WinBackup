namespace WinBackup.Core.FileHistory;

public enum FileHistoryState
{
    Enabled,
    Disabled,
    NotConfigured,
}

/// <summary>Backup cadence options exposed by Windows File History.</summary>
public enum FhFrequency
{
    Every10Minutes,
    Every15Minutes,
    Every20Minutes,
    Every30Minutes,
    Hourly,
    Every3Hours,
    Every6Hours,
    Every12Hours,
    Daily,
}

/// <summary>How long File History keeps saved versions.</summary>
public enum FhRetention
{
    UntilSpaceIsNeeded,
    OneMonth,
    ThreeMonths,
    SixMonths,
    NineMonths,
    OneYear,
    TwoYears,
    Forever,
}

/// <summary>Snapshot of the current File History configuration for the UI.</summary>
public sealed record FileHistoryStatus(
    FileHistoryState State,
    DateTimeOffset? LastBackupTime,
    string? TargetDriveLabel,
    long? TargetFreeBytes)
{
    public static FileHistoryStatus NotConfigured { get; } =
        new(FileHistoryState.NotConfigured, null, null, null);
}

/// <summary>Result of a File History mutation (enable/disable, set frequency/retention, back up now).</summary>
public sealed record FhActionResult(bool Success, string? Error = null)
{
    public static FhActionResult Ok { get; } = new(true);

    public static FhActionResult Fail(string error) => new(false, error);
}
