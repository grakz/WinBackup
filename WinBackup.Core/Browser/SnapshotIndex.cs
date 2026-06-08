using WinBackup.Core.Abstractions;

namespace WinBackup.Core.Browser;

/// <summary>
/// Maps a source folder to its Windows File History versions folder and reports whether File History
/// is currently reachable. The real implementation derives the archive path from the FH drive +
/// volume; tests supply a fake.
/// </summary>
public interface IFileHistoryLocator
{
    bool IsConnected { get; }

    /// <summary>FH versions folder for <paramref name="sourceFolder"/>, or null if FH has nothing for it.</summary>
    string? GetVersionsFolder(string sourceFolder);
}

/// <summary>
/// Unifies the SSD and File History timelines and routes a "contents at time T" request to the best
/// source: File History when it has a version at/before T (fast, no SSD needed), otherwise the SSD
/// (older snapshots), otherwise reports why nothing is available.
/// </summary>
public sealed class SnapshotIndex
{
    private readonly IFileSystem _fs;
    private readonly SsdSnapshotReader _ssd;
    private readonly FileHistoryReader _fh;
    private readonly IFileHistoryLocator _fhLocator;
    private readonly Func<string?> _resolveSsdRoot;

    public SnapshotIndex(
        IFileSystem fs,
        SsdSnapshotReader ssd,
        FileHistoryReader fh,
        IFileHistoryLocator fhLocator,
        Func<string?> resolveSsdRoot)
    {
        _fs = fs;
        _ssd = ssd;
        _fh = fh;
        _fhLocator = fhLocator;
        _resolveSsdRoot = resolveSsdRoot;
    }

    private static string LeafOf(string sourceFolder) =>
        Path.GetFileName(sourceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>Merged, chronologically sorted timeline (newest first) from both sources.</summary>
    public IReadOnlyList<BackupSnapshot> GetTimeline(string sourceFolder)
    {
        var all = new List<BackupSnapshot>();

        string? ssdRoot = _resolveSsdRoot();
        if (ssdRoot is not null)
        {
            all.AddRange(_ssd.ListSnapshots(ssdRoot, LeafOf(sourceFolder)));
        }

        if (_fhLocator.IsConnected && _fhLocator.GetVersionsFolder(sourceFolder) is { } fhFolder)
        {
            all.AddRange(_fh.ListSnapshots(fhFolder));
        }

        return all.OrderByDescending(s => s.Timestamp).ToList();
    }

    /// <summary>Reconstructs <paramref name="sourceFolder"/> at <paramref name="at"/>, choosing the best source.</summary>
    public SnapshotContents GetContentsAt(string sourceFolder, DateTimeOffset at)
    {
        // Prefer File History when it actually holds a version at/before T (no SSD spin-up needed).
        if (_fhLocator.IsConnected && _fhLocator.GetVersionsFolder(sourceFolder) is { } fhFolder)
        {
            SnapshotContents fhContents = _fh.GetContentsAt(fhFolder, at);
            if (fhContents.IsAvailable && fhContents.Files.Count > 0)
            {
                return fhContents;
            }
        }

        // Fall back to the SSD for older snapshots.
        string? ssdRoot = _resolveSsdRoot();
        if (ssdRoot is not null && _fs.DirectoryExists(ssdRoot))
        {
            return _ssd.GetContentsAt(ssdRoot, LeafOf(sourceFolder), at);
        }

        return SnapshotContents.Unavailable(
            "No connected backup source has data for that date. Connect your SSD to access older snapshots.");
    }
}
