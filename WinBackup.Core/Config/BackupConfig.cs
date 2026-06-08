namespace WinBackup.Core.Config;

/// <summary>
/// Root configuration model. Persisted as <c>config.json</c> in <c>%APPDATA%\WinBackup\</c>.
/// All members have safe defaults so a missing or partial file still yields a usable config.
/// </summary>
public sealed class BackupConfig
{
    public List<string> SourceFolders { get; set; } = new();

    public SsdConfig Ssd { get; set; } = new();

    public ProtonConfig Proton { get; set; } = new();

    public ScheduleConfig Schedule { get; set; } = new();

    /// <summary>User-supplied glob patterns excluded from backup, in addition to the built-in defaults.</summary>
    public List<string> ExcludePatterns { get; set; } = new();

    public string LogDir { get; set; } = string.Empty;
}

public sealed class SsdConfig
{
    public string VolumeLabel { get; set; } = "BACKUP_SSD";

    public string DiskSerial { get; set; } = string.Empty;

    public string BackupSubdir { get; set; } = "Backups";

    public bool DismountAfterBackup { get; set; } = true;

    public int ConnectWaitMinutes { get; set; } = 30;
}

public sealed class ProtonConfig
{
    public string SyncFolder { get; set; } = string.Empty;

    /// <summary>
    /// The Proton cutoff uses the Nth-most-recent successful SSD backup so a missed monthly
    /// SSD run does not create a data-loss gap. N is this value.
    /// </summary>
    public int LookbackBackups { get; set; } = 2;
}

public sealed class ScheduleConfig
{
    /// <summary>Day of month (1-28) the monthly SSD backup is due.</summary>
    public int SsdDayOfMonth { get; set; } = 1;

    /// <summary>SSD backup time, "HH:MM" 24-hour.</summary>
    public string SsdTime { get; set; } = "18:00";

    /// <summary>Proton backup time, "HH:MM" 24-hour.</summary>
    public string ProtonTime { get; set; } = "02:00";
}
