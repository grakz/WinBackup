namespace WinBackup.Core.Abstractions;

/// <summary>Real-filesystem implementation of <see cref="IFileSystem"/> over <see cref="System.IO"/>.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public IEnumerable<FileItem> EnumerateFiles(string root)
    {
        // Enumerating placeholder files does NOT hydrate them — FindFirstFile/FindNextFile
        // (which this wraps) returns placeholder metadata without triggering a download.
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (string path in Directory.EnumerateFiles(root, "*", options))
        {
            yield return GetItem(path);
        }
    }

    public FileItem GetItem(string path)
    {
        var info = new FileInfo(path);
        return new FileItem(
            path,
            info.Length,
            info.LastWriteTimeUtc,
            info.Attributes);
    }

    public Stream OpenRead(string path, FileShare share) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, share);

    public Stream OpenWrite(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
    }

    public long GetAvailableFreeSpace(string path)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(path))
            ?? throw new ArgumentException($"Cannot determine volume root for '{path}'.", nameof(path));
        return new DriveInfo(root).AvailableFreeSpace;
    }
}
