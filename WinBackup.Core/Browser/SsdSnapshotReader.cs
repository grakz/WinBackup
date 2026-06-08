using System.Text.RegularExpressions;
using WinBackup.Core.Abstractions;

namespace WinBackup.Core.Browser;

/// <summary>
/// Reads the SSD backup layout and reconstructs a folder's state at a point in time.
/// <para>
/// Layout: <c>{ssdRoot}\{YYYY}_FULL\{leaf}\…</c> is the yearly baseline; each
/// <c>{ssdRoot}\{YYYY-MM}_INCR\{leaf}\…</c> adds files modified since the prior backup. Reconstruction
/// at time T = the newest FULL at/before T, then every INCR up to T applied in order (later wins).
/// </para>
/// <para>
/// The backup format is additive (it never records deletions), so reconstruction yields the latest
/// backed-up version of every file captured up to T — a conservative choice for a backup tool.
/// </para>
/// </summary>
public sealed class SsdSnapshotReader
{
    private static readonly Regex FullRx = new(@"^(\d{4})_FULL$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IncrRx = new(@"^(\d{4})-(\d{2})_INCR$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IFileSystem _fs;

    public SsdSnapshotReader(IFileSystem fs) => _fs = fs ?? throw new ArgumentNullException(nameof(fs));

    private sealed record Layer(string FolderName, DateTimeOffset Timestamp, bool IsFull);

    /// <summary>Lists the SSD snapshots that contain data for <paramref name="leaf"/> (the source folder name).</summary>
    public IReadOnlyList<BackupSnapshot> ListSnapshots(string ssdRoot, string leaf)
    {
        if (!_fs.DirectoryExists(ssdRoot))
        {
            return Array.Empty<BackupSnapshot>();
        }

        return EnumerateLayers(ssdRoot, leaf)
            .OrderBy(l => l.Timestamp)
            .ThenBy(l => l.IsFull ? 0 : 1)
            .Select(l => new BackupSnapshot(
                l.Timestamp,
                l.IsFull ? SnapshotSource.SsdFull : SnapshotSource.SsdIncremental,
                l.FolderName,
                IsAvailable: true))
            .ToList();
    }

    /// <summary>Reconstructs the contents of <paramref name="leaf"/> as they were at <paramref name="at"/>.</summary>
    public SnapshotContents GetContentsAt(string ssdRoot, string leaf, DateTimeOffset at)
    {
        if (!_fs.DirectoryExists(ssdRoot))
        {
            return SnapshotContents.Unavailable("SSD not connected.");
        }

        List<Layer> layers = EnumerateLayers(ssdRoot, leaf)
            .Where(l => l.Timestamp <= at)
            .ToList();

        Layer? baseFull = layers.Where(l => l.IsFull).OrderByDescending(l => l.Timestamp).FirstOrDefault();
        IEnumerable<Layer> applicable = baseFull is null
            ? layers // no full baseline: best-effort over incrementals
            : layers.Where(l => l.Timestamp >= baseFull.Timestamp);

        var merged = new Dictionary<string, SnapshotFile>(StringComparer.OrdinalIgnoreCase);
        foreach (Layer layer in applicable.OrderBy(l => l.Timestamp).ThenBy(l => l.IsFull ? 0 : 1))
        {
            string layerLeafDir = Path.Combine(ssdRoot, layer.FolderName, leaf);
            if (!_fs.DirectoryExists(layerLeafDir))
            {
                continue;
            }

            SnapshotSource source = layer.IsFull ? SnapshotSource.SsdFull : SnapshotSource.SsdIncremental;
            foreach (FileItem file in _fs.EnumerateFiles(layerLeafDir))
            {
                string relative = Path.GetRelativePath(layerLeafDir, file.Path);
                merged[relative] = new SnapshotFile(relative, file.Path, file.Length, file.LastWriteTimeUtc, source);
            }
        }

        return new SnapshotContents(merged.Values.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private IEnumerable<Layer> EnumerateLayers(string ssdRoot, string leaf)
    {
        // Discover top-level layer folders by inspecting the first path segment of each backed-up file.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FileItem file in _fs.EnumerateFiles(ssdRoot))
        {
            string relative = Path.GetRelativePath(ssdRoot, file.Path);
            string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Length < 2)
            {
                continue;
            }

            string folder = parts[0];
            string fileLeaf = parts[1];
            if (!fileLeaf.Equals(leaf, StringComparison.OrdinalIgnoreCase) || !seen.Add(folder))
            {
                continue;
            }

            if (TryParseLayer(folder, out Layer? layer))
            {
                yield return layer!;
            }
        }
    }

    private static bool TryParseLayer(string folderName, out Layer? layer)
    {
        Match full = FullRx.Match(folderName);
        if (full.Success)
        {
            int year = int.Parse(full.Groups[1].Value);
            layer = new Layer(folderName, new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero), IsFull: true);
            return true;
        }

        Match incr = IncrRx.Match(folderName);
        if (incr.Success)
        {
            int year = int.Parse(incr.Groups[1].Value);
            int month = int.Parse(incr.Groups[2].Value);
            layer = new Layer(folderName, new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero), IsFull: false);
            return true;
        }

        layer = null;
        return false;
    }
}
