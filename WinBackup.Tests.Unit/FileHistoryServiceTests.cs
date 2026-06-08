using WinBackup.Core.FileHistory;
using WinBackup.Tests.Unit.Fakes;
using Xunit;

namespace WinBackup.Tests.Unit;

public sealed class FileHistoryServiceTests
{
    [Fact]
    public void GetStatus_Enabled_ReportsDriveAndLastBackup()
    {
        var backend = new FakeFileHistoryBackend
        {
            IsEnabled = true,
            TargetDriveLabel = "HISTORY",
            TargetFreeBytes = 123_456,
            LastBackupTime = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
        };
        var sut = new FileHistoryService(backend);

        FileHistoryStatus status = sut.GetStatus();

        Assert.Equal(FileHistoryState.Enabled, status.State);
        Assert.Equal("HISTORY", status.TargetDriveLabel);
        Assert.Equal(123_456, status.TargetFreeBytes);
        Assert.NotNull(status.LastBackupTime);
    }

    [Fact]
    public void GetStatus_NotConfigured_ReportsNotConfigured()
    {
        var sut = new FileHistoryService(new FakeFileHistoryBackend { IsConfigured = false });

        Assert.Equal(FileHistoryState.NotConfigured, sut.GetStatus().State);
    }

    [Fact]
    public void GetStatus_BackendThrows_ReturnsNotConfiguredWithoutThrowing()
    {
        // IsAvailable getter throwing simulates a broken COM server.
        var sut = new FileHistoryService(new ThrowingBackend());

        Assert.Equal(FileHistoryState.NotConfigured, sut.GetStatus().State);
    }

    [Fact]
    public void SetEnabled_TogglesState()
    {
        var backend = new FakeFileHistoryBackend { IsEnabled = false };
        var sut = new FileHistoryService(backend);

        FhActionResult result = sut.SetEnabled(true);

        Assert.True(result.Success);
        Assert.True(backend.IsEnabled);
    }

    [Fact]
    public void SetEnabled_NotConfigured_FailsGracefully()
    {
        var sut = new FileHistoryService(new FakeFileHistoryBackend { IsConfigured = false });

        FhActionResult result = sut.SetEnabled(true);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void SetFrequencyAndRetention_RoundTrip()
    {
        var backend = new FakeFileHistoryBackend();
        var sut = new FileHistoryService(backend);

        Assert.True(sut.SetFrequency(FhFrequency.Every12Hours).Success);
        Assert.True(sut.SetRetention(FhRetention.OneYear).Success);

        Assert.Equal(FhFrequency.Every12Hours, sut.GetFrequency());
        Assert.Equal(FhRetention.OneYear, sut.GetRetention());
    }

    [Fact]
    public void TriggerBackupNow_CallsBackend()
    {
        var backend = new FakeFileHistoryBackend();
        var sut = new FileHistoryService(backend);

        Assert.True(sut.TriggerBackupNow().Success);
        Assert.Equal(1, backend.TriggerCount);
    }

    [Fact]
    public void Mutation_BackendThrows_ReturnsErrorResult()
    {
        var backend = new FakeFileHistoryBackend { ThrowOnMutate = true };
        var sut = new FileHistoryService(backend);

        FhActionResult result = sut.TriggerBackupNow();

        Assert.False(result.Success);
        Assert.Equal("COM failure", result.Error);
    }

    private sealed class ThrowingBackend : IFileHistoryBackend
    {
        public bool IsAvailable => throw new InvalidOperationException("COM unavailable");
        public bool IsConfigured => false;
        public bool IsEnabled => false;
        public void SetEnabled(bool enabled) => throw new InvalidOperationException();
        public FhFrequency Frequency { get => default; set => throw new InvalidOperationException(); }
        public FhRetention Retention { get => default; set => throw new InvalidOperationException(); }
        public DateTimeOffset? LastBackupTime => null;
        public string? TargetDriveLabel => null;
        public long? TargetFreeBytes => null;
        public void TriggerBackup() => throw new InvalidOperationException();
    }
}
