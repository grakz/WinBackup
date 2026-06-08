using WinBackup.Core.Abstractions;

namespace WinBackup.Core.OneDrive;

/// <summary>A file discovered under a OneDrive folder and whether it is a cloud-only placeholder.</summary>
public sealed record OneDriveEntry(string Path, bool IsCloudOnly, long SizeBytes);

/// <summary>
/// Enumerates files under a folder and classifies each as local or cloud-only (OneDrive Files
/// On Demand). Enumeration reads placeholder metadata only — it does <em>not</em> open files, so it
/// never triggers a hydration/download.
/// </summary>
public sealed class OneDriveFileEnumerator
{
    private readonly IFileSystem _fs;

    public OneDriveFileEnumerator(IFileSystem fs) => _fs = fs ?? throw new ArgumentNullException(nameof(fs));

    public IEnumerable<OneDriveEntry> Enumerate(string folder)
    {
        foreach (FileItem item in _fs.EnumerateFiles(folder))
        {
            yield return new OneDriveEntry(item.Path, item.IsCloudOnly, item.Length);
        }
    }
}
