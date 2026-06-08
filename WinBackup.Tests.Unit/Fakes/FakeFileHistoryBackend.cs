using WinBackup.Core.FileHistory;

namespace WinBackup.Tests.Unit.Fakes;

public sealed class FakeFileHistoryBackend : IFileHistoryBackend
{
    private FhFrequency _frequency = FhFrequency.Hourly;
    private FhRetention _retention = FhRetention.UntilSpaceIsNeeded;

    public bool IsAvailable { get; set; } = true;
    public bool IsConfigured { get; set; } = true;
    public bool IsEnabled { get; set; }

    public FhFrequency Frequency
    {
        get => _frequency;
        set { if (ThrowOnMutate) throw new InvalidOperationException("COM failure"); _frequency = value; }
    }

    public FhRetention Retention
    {
        get => _retention;
        set { if (ThrowOnMutate) throw new InvalidOperationException("COM failure"); _retention = value; }
    }

    public DateTimeOffset? LastBackupTime { get; set; }
    public string? TargetDriveLabel { get; set; }
    public long? TargetFreeBytes { get; set; }

    /// <summary>When set, every mutating call throws to simulate a COM failure.</summary>
    public bool ThrowOnMutate { get; set; }

    public int TriggerCount { get; private set; }

    public void SetEnabled(bool enabled)
    {
        if (ThrowOnMutate) throw new InvalidOperationException("COM failure");
        IsEnabled = enabled;
    }

    public void TriggerBackup()
    {
        if (ThrowOnMutate) throw new InvalidOperationException("COM failure");
        TriggerCount++;
    }
}
