using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WinBackup.Core.Backup;

namespace WinBackup.Core.Volume;

/// <summary>
/// <see cref="ILockedFileHandler"/> that copies a locked file from a VSS shadow copy. Obtains the
/// shadow root for the file's volume via <see cref="IVssCoordinator"/>, maps the file onto the
/// snapshot, and copies it with the normal verified <see cref="FileCopyService"/>.
/// </summary>
public sealed class VssLockedFileHandler : ILockedFileHandler
{
    private readonly IVssCoordinator _coordinator;
    private readonly FileCopyService _copy;
    private readonly ILogger _log;

    public VssLockedFileHandler(IVssCoordinator coordinator, FileCopyService copy, ILogger? log = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _copy = copy ?? throw new ArgumentNullException(nameof(copy));
        _log = log ?? NullLogger.Instance;
    }

    public async Task<bool> TryCopyLockedAsync(
        string source,
        string dest,
        IProgress<FileCopyProgress>? progress,
        CancellationToken ct)
    {
        string volume = Path.GetPathRoot(source) ?? string.Empty;
        string? shadowRoot = await _coordinator.GetShadowRootAsync(volume, ct).ConfigureAwait(false);
        if (shadowRoot is null)
        {
            _log.LogWarning("No VSS snapshot available for {Volume}; cannot copy locked file {Path}", volume, source);
            return false;
        }

        string shadowSource = ShadowPath.Map(shadowRoot, source);
        try
        {
            FileCopyResult result = await _copy.CopyAsync(shadowSource, dest, progress, ct).ConfigureAwait(false);
            return result.Outcome == FileCopyOutcome.Copied;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "VSS copy failed for {Path}", source);
            return false;
        }
    }
}
