namespace WinBackup.Core.Volume;

/// <summary>Maps a normal file path onto a VSS shadow-copy device path.</summary>
public static class ShadowPath
{
    /// <summary>
    /// Combines a shadow device root (e.g. <c>\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1</c>)
    /// with the volume-relative portion of <paramref name="originalPath"/> (e.g. <c>C:\Users\me\a.txt</c>
    /// → <c>Users\me\a.txt</c>), yielding the path to read the file from the snapshot.
    /// </summary>
    public static string Map(string shadowDeviceRoot, string originalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shadowDeviceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);

        string root = Path.GetPathRoot(originalPath)
            ?? throw new ArgumentException($"'{originalPath}' has no volume root.", nameof(originalPath));

        string relative = originalPath[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return shadowDeviceRoot.TrimEnd('\\') + "\\" + relative;
    }
}
