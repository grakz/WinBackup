namespace WinBackup.Core.FileHistory;

/// <summary>
/// Thin seam over the Windows File History COM API (<c>FhConfigMgr</c> / <c>IFhConfigMgr</c>).
/// The real implementation lives in the app project (CsWin32 COM interop); this interface lets
/// <see cref="FileHistoryService"/> be unit-tested without the COM server present.
/// </summary>
public interface IFileHistoryBackend
{
    /// <summary>True when the File History service/COM object is present on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>True when a backup target drive has been configured.</summary>
    bool IsConfigured { get; }

    bool IsEnabled { get; }

    void SetEnabled(bool enabled);

    FhFrequency Frequency { get; set; }

    FhRetention Retention { get; set; }

    DateTimeOffset? LastBackupTime { get; }

    string? TargetDriveLabel { get; }

    long? TargetFreeBytes { get; }

    void TriggerBackup();
}
