using WinBackup.Core.State;
using Xunit;

namespace WinBackup.Tests.Unit;

public sealed class StateServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;
    private readonly StateService _sut = new();

    public StateServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "winbackup-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "state.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static BackupRecord Record(
        BackupTarget target,
        BackupResultCode code,
        DateTimeOffset completedAt,
        int files = 10) => new()
    {
        Target = target,
        ResultCode = code,
        StartedAt = completedAt.AddMinutes(-5),
        CompletedAt = completedAt,
        FilesCopiedCount = files,
    };

    [Fact]
    public void Load_MissingFile_ReturnsEmptyState()
    {
        BackupState state = _sut.Load(_path);

        Assert.NotNull(state);
        Assert.Empty(state.Records);
    }

    [Fact]
    public void AddRecord_AppendsAndPersists()
    {
        _sut.AddRecord(_path, Record(BackupTarget.Proton, BackupResultCode.Success, DateTimeOffset.UtcNow));
        _sut.AddRecord(_path, Record(BackupTarget.Ssd, BackupResultCode.Success, DateTimeOffset.UtcNow));

        BackupState reloaded = _sut.Load(_path);

        Assert.Equal(2, reloaded.Records.Count);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var record = new BackupRecord
        {
            Target = BackupTarget.Ssd,
            Kind = BackupKind.Incremental,
            StartedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 1, 2, 3, 9, 5, TimeSpan.Zero),
            ResultCode = BackupResultCode.PartialSuccess,
            FilesCopiedCount = 42,
            VssFallbackCount = 3,
            ExcludedCount = 7,
            SkippedCount = 1,
            ErrorMessage = "one file skipped",
        };
        _sut.Save(_path, new BackupState { Records = { record } });

        BackupRecord loaded = Assert.Single(_sut.Load(_path).Records);

        Assert.Equal(BackupTarget.Ssd, loaded.Target);
        Assert.Equal(BackupKind.Incremental, loaded.Kind);
        Assert.Equal(record.StartedAt, loaded.StartedAt);
        Assert.Equal(record.CompletedAt, loaded.CompletedAt);
        Assert.Equal(BackupResultCode.PartialSuccess, loaded.ResultCode);
        Assert.Equal(42, loaded.FilesCopiedCount);
        Assert.Equal(3, loaded.VssFallbackCount);
        Assert.Equal(7, loaded.ExcludedCount);
        Assert.Equal(1, loaded.SkippedCount);
        Assert.Equal("one file skipped", loaded.ErrorMessage);
    }

    [Fact]
    public void GetLastSuccessful_NoRecords_ReturnsNull()
    {
        Assert.Null(_sut.GetLastSuccessful(new BackupState(), BackupTarget.Ssd));
    }

    [Fact]
    public void GetLastSuccessful_IgnoresFailedAndOtherTargets()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var state = new BackupState
        {
            Records =
            {
                Record(BackupTarget.Ssd, BackupResultCode.Failed, now),               // failed → ignored
                Record(BackupTarget.Proton, BackupResultCode.Success, now),           // wrong target → ignored
                Record(BackupTarget.Ssd, BackupResultCode.Success, now.AddDays(-10)),  // older success
                Record(BackupTarget.Ssd, BackupResultCode.PartialSuccess, now.AddDays(-2)), // newer partial success
            },
        };

        BackupRecord? result = _sut.GetLastSuccessful(state, BackupTarget.Ssd);

        Assert.NotNull(result);
        Assert.Equal(now.AddDays(-2), result!.CompletedAt);
    }
}
