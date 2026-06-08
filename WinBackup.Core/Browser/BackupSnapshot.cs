namespace WinBackup.Core.Browser;

/// <summary>Where a browsable snapshot/version comes from.</summary>
public enum SnapshotSource
{
    SsdFull,
    SsdIncremental,
    FileHistory,
}

/// <summary>A point-in-time snapshot available for browsing on the timeline.</summary>
public sealed record BackupSnapshot(
    DateTimeOffset Timestamp,
    SnapshotSource Source,
    string Label,
    bool IsAvailable);

/// <summary>One file as it existed in a reconstructed snapshot, with the physical path to restore from.</summary>
public sealed record SnapshotFile(
    string RelativePath,
    string PhysicalPath,
    long SizeBytes,
    DateTimeOffset ModifiedUtc,
    SnapshotSource Source);

/// <summary>
/// The reconstructed contents of a folder at a point in time. <see cref="UnavailableReason"/> is
/// non-null when the contents could not be produced (e.g. the SSD is not connected).
/// </summary>
public sealed record SnapshotContents(IReadOnlyList<SnapshotFile> Files, string? UnavailableReason = null)
{
    public bool IsAvailable => UnavailableReason is null;

    public static SnapshotContents Unavailable(string reason) => new(Array.Empty<SnapshotFile>(), reason);
}
