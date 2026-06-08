using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WinBackup.Core.FileHistory;

/// <summary>
/// High-level façade over Windows File History. Every operation degrades gracefully: if the File
/// History service is missing, no drive is configured, or a COM call fails, callers get a sensible
/// status or a failed <see cref="FhActionResult"/> rather than an exception.
/// </summary>
public sealed class FileHistoryService
{
    private readonly IFileHistoryBackend _backend;
    private readonly ILogger _log;

    public FileHistoryService(IFileHistoryBackend backend, ILogger? log = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _log = log ?? NullLogger.Instance;
    }

    public FileHistoryStatus GetStatus()
    {
        try
        {
            if (!_backend.IsAvailable || !_backend.IsConfigured)
            {
                return FileHistoryStatus.NotConfigured;
            }

            FileHistoryState state = _backend.IsEnabled ? FileHistoryState.Enabled : FileHistoryState.Disabled;
            return new FileHistoryStatus(state, _backend.LastBackupTime, _backend.TargetDriveLabel, _backend.TargetFreeBytes);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Reading File History status failed.");
            return FileHistoryStatus.NotConfigured;
        }
    }

    public FhActionResult SetEnabled(bool enabled) => Guarded(() => _backend.SetEnabled(enabled));

    public FhActionResult SetFrequency(FhFrequency frequency) => Guarded(() => _backend.Frequency = frequency);

    public FhActionResult SetRetention(FhRetention retention) => Guarded(() => _backend.Retention = retention);

    public FhActionResult TriggerBackupNow() => Guarded(_backend.TriggerBackup);

    public FhFrequency? GetFrequency() => TryRead(() => _backend.Frequency);

    public FhRetention? GetRetention() => TryRead(() => _backend.Retention);

    private FhActionResult Guarded(Action action)
    {
        if (!_backend.IsAvailable || !_backend.IsConfigured)
        {
            return FhActionResult.Fail("File History is not configured. Choose a backup drive in Windows Settings.");
        }

        try
        {
            action();
            return FhActionResult.Ok;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "File History operation failed.");
            return FhActionResult.Fail(ex.Message);
        }
    }

    private T? TryRead<T>(Func<T> read) where T : struct
    {
        if (!_backend.IsAvailable || !_backend.IsConfigured)
        {
            return null;
        }

        try
        {
            return read();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Reading File History value failed.");
            return null;
        }
    }
}
