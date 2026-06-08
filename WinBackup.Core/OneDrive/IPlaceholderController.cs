namespace WinBackup.Core.OneDrive;

/// <summary>
/// Controls OneDrive placeholder hydration state. The real implementation calls
/// <c>CfSetPinState(CF_PIN_STATE_UNPINNED)</c> via the Cloud Filter API to re-dehydrate a file
/// after it has been backed up, so backing up cloud-only files does not permanently fill the disk.
/// </summary>
public interface IPlaceholderController
{
    /// <summary>Requests that the file at <paramref name="path"/> be returned to cloud-only state.</summary>
    void Dehydrate(string path);
}

/// <summary>No-op controller used when OneDrive dehydration is not applicable (e.g. local-only sources, tests).</summary>
public sealed class NullPlaceholderController : IPlaceholderController
{
    public static readonly NullPlaceholderController Instance = new();

    public void Dehydrate(string path) { /* nothing to do */ }
}
