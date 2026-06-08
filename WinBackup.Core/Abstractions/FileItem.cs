namespace WinBackup.Core.Abstractions;

/// <summary>Metadata for a single file, independent of the physical filesystem (so it can be faked in tests).</summary>
public sealed record FileItem(
    string Path,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    FileAttributes Attributes)
{
    /// <summary>
    /// True when the file is an OneDrive "Files On Demand" cloud-only placeholder
    /// (<see cref="FileAttributes"/> includes the recall-on-data-access flag, 0x00400000).
    /// </summary>
    public bool IsCloudOnly => ((int)Attributes & RecallOnDataAccess) != 0;

    /// <summary>FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS — not exposed by the <see cref="FileAttributes"/> enum on net8.</summary>
    public const int RecallOnDataAccess = 0x00400000;
}
