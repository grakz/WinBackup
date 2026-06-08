using System.Globalization;
using System.Text.RegularExpressions;
using WinBackup.Core.Abstractions;

namespace WinBackup.Core.Browser;

/// <summary>
/// Reads the Windows File History archive. Versions are stored as files named
/// <c>OriginalName (YYYY_MM_DD HH_MM_SS UTC).ext</c> inside a mirror of the source tree. This reader
/// parses those version stamps to list and reconstruct historical versions of files.
/// </summary>
public sealed class FileHistoryReader
{
    // e.g. "Report (2026_01_15 10_30_00 UTC).docx"
    private static readonly Regex VersionRx = new(
        @"^(?<base>.*) \((?<ts>\d{4}_\d{2}_\d{2} \d{2}_\d{2}_\d{2}) UTC\)(?<ext>\.[^.\\/]*)?$",
        RegexOptions.Compiled);

    private const string TsFormat = "yyyy_MM_dd HH_mm_ss";

    private readonly IFileSystem _fs;

    public FileHistoryReader(IFileSystem fs) => _fs = fs ?? throw new ArgumentNullException(nameof(fs));

    /// <summary>A single archived version of a file.</summary>
    public sealed record FhVersion(string OriginalName, DateTimeOffset Timestamp, string PhysicalPath, long SizeBytes);

    /// <summary>Parses a File History version filename. Returns false for non-versioned names.</summary>
    public static bool TryParseVersion(string fileName, out string originalName, out DateTimeOffset timestamp)
    {
        originalName = string.Empty;
        timestamp = default;

        Match m = VersionRx.Match(fileName);
        if (!m.Success)
        {
            return false;
        }

        if (!DateTimeOffset.TryParseExact(m.Groups["ts"].Value, TsFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp))
        {
            return false;
        }

        string ext = m.Groups["ext"].Success ? m.Groups["ext"].Value : string.Empty;
        originalName = m.Groups["base"].Value + ext;
        return true;
    }

    /// <summary>All versions in <paramref name="versionsFolder"/>, newest first.</summary>
    public IReadOnlyList<FhVersion> ListAllVersions(string versionsFolder)
    {
        if (!_fs.DirectoryExists(versionsFolder))
        {
            return Array.Empty<FhVersion>();
        }

        var list = new List<FhVersion>();
        foreach (FileItem file in _fs.EnumerateFiles(versionsFolder))
        {
            if (TryParseVersion(Path.GetFileName(file.Path), out string original, out DateTimeOffset ts))
            {
                list.Add(new FhVersion(original, ts, file.Path, file.Length));
            }
        }

        return list.OrderByDescending(v => v.Timestamp).ToList();
    }

    /// <summary>Versions of one specific original file, newest first.</summary>
    public IReadOnlyList<FhVersion> ListVersions(string versionsFolder, string originalName) =>
        ListAllVersions(versionsFolder)
            .Where(v => v.OriginalName.Equals(originalName, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>Physical path of the newest version of <paramref name="originalName"/> at or before <paramref name="at"/>.</summary>
    public string? GetVersionPath(string versionsFolder, string originalName, DateTimeOffset at) =>
        ListVersions(versionsFolder, originalName).FirstOrDefault(v => v.Timestamp <= at)?.PhysicalPath;

    /// <summary>Distinct capture times in this folder, as browsable snapshots.</summary>
    public IReadOnlyList<BackupSnapshot> ListSnapshots(string versionsFolder) =>
        ListAllVersions(versionsFolder)
            .Select(v => v.Timestamp)
            .Distinct()
            .OrderByDescending(ts => ts)
            .Select(ts => new BackupSnapshot(ts, SnapshotSource.FileHistory, ts.ToString("u"), IsAvailable: true))
            .ToList();

    /// <summary>Reconstructs the folder contents at <paramref name="at"/>: newest version ≤ T of each file.</summary>
    public SnapshotContents GetContentsAt(string versionsFolder, DateTimeOffset at)
    {
        if (!_fs.DirectoryExists(versionsFolder))
        {
            return SnapshotContents.Unavailable("File History folder not available.");
        }

        var byOriginal = ListAllVersions(versionsFolder)
            .Where(v => v.Timestamp <= at)
            .GroupBy(v => v.OriginalName, StringComparer.OrdinalIgnoreCase);

        var files = new List<SnapshotFile>();
        foreach (var group in byOriginal)
        {
            FhVersion newest = group.OrderByDescending(v => v.Timestamp).First();
            files.Add(new SnapshotFile(newest.OriginalName, newest.PhysicalPath, newest.SizeBytes, newest.Timestamp, SnapshotSource.FileHistory));
        }

        return new SnapshotContents(files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList());
    }
}
