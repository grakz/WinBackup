namespace WinBackup.Core.State;

/// <summary>Which backup target a record describes.</summary>
public enum BackupTarget
{
    Ssd,
    Proton,
}

/// <summary>Outcome of a backup run.</summary>
public enum BackupResultCode
{
    Success,
    PartialSuccess,
    Failed,
}

/// <summary>The type of copy performed (used by the browser timeline and status UI).</summary>
public enum BackupKind
{
    Full,
    Incremental,
    Delta,
}

/// <summary>A single completed (or attempted) backup run.</summary>
public sealed class BackupRecord
{
    public BackupTarget Target { get; set; }

    public BackupKind Kind { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public BackupResultCode ResultCode { get; set; }

    /// <summary>Number of files successfully copied and verified.</summary>
    public int FilesCopiedCount { get; set; }

    /// <summary>Files copied via the VSS snapshot fallback because they were locked.</summary>
    public int VssFallbackCount { get; set; }

    /// <summary>Files excluded by the temp/lock filter (not errors).</summary>
    public int ExcludedCount { get; set; }

    /// <summary>Files that could not be copied by any method.</summary>
    public int SkippedCount { get; set; }

    public string? ErrorMessage { get; set; }
}

/// <summary>Root persisted state: the full history of backup runs.</summary>
public sealed class BackupState
{
    public List<BackupRecord> Records { get; set; } = new();
}
