namespace WinBackup.Core.Abstractions;

/// <summary>
/// Filesystem seam used by every backup engine so logic can be unit-tested against an
/// in-memory fake. The physical implementation is <see cref="PhysicalFileSystem"/>.
/// </summary>
public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    /// <summary>Recursively enumerates all files under <paramref name="root"/> with their metadata.</summary>
    IEnumerable<FileItem> EnumerateFiles(string root);

    FileItem GetItem(string path);

    /// <summary>
    /// Opens a file for reading with the given share mode. Throws <see cref="IOException"/> with a
    /// sharing-violation HRESULT when the file is locked and <paramref name="share"/> cannot be honoured.
    /// </summary>
    Stream OpenRead(string path, FileShare share);

    /// <summary>Creates or truncates a file for writing, creating parent directories as needed.</summary>
    Stream OpenWrite(string path);

    /// <summary>Free bytes available to the caller on the volume containing <paramref name="path"/>.</summary>
    long GetAvailableFreeSpace(string path);
}
