using WinBackup.Core.Backup;
using WinBackup.Core.Config;
using WinBackup.Core.State;
using WinBackup.Tests.Unit.Fakes;
using Xunit;

namespace WinBackup.Tests.Unit;

public sealed class SsdBackupEngineTests : IDisposable
{
    private readonly string _dir;
    private readonly string _statePath;
    private readonly StateService _state = new();

    public SsdBackupEngineTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "winbackup-ssd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _statePath = Path.Combine(_dir, "state.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private (SsdBackupEngine engine, FakeFileSystem fs) Build(FakeClock clock, FakeFileSystem? fs = null)
    {
        fs ??= new FakeFileSystem();
        var config = new BackupConfig { SourceFolders = { @"C:\src\Documents" } };
        var copier = new FileSetCopier(new FileCopyService(fs, new FileFilterService(), maxRetries: 0, retryDelay: TimeSpan.Zero));
        var engine = new SsdBackupEngine(fs, copier, _state, _statePath, clock, config);
        return (engine, fs);
    }

    [Fact]
    public async Task FirstRunOfYear_PerformsFullCopy()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 1, 18, 0, 0, TimeSpan.Zero));
        var (engine, fs) = Build(clock);
        fs.AddFile(@"C:\src\Documents\a.txt", "one");
        fs.AddFile(@"C:\src\Documents\sub\b.txt", "two");

        BackupRecord record = await engine.RunAsync(@"X:\Backups");

        Assert.Equal(BackupKind.Full, record.Kind);
        Assert.Equal(2, record.FilesCopiedCount);
        Assert.Equal(BackupResultCode.Success, record.ResultCode);
        Assert.NotNull(fs.GetContent(@"X:\Backups\2026_FULL\Documents\a.txt"));
        Assert.NotNull(fs.GetContent(@"X:\Backups\2026_FULL\Documents\sub\b.txt"));
    }

    [Fact]
    public async Task SecondRunSameYear_PerformsIncrementalOfModifiedFilesOnly()
    {
        // First (full) run in March.
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 1, 18, 0, 0, TimeSpan.Zero));
        var (engine, fs) = Build(clock);
        fs.AddFile(@"C:\src\Documents\old.txt", "old", lastWriteUtc: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        await engine.RunAsync(@"X:\Backups");

        // April run: one new file modified after the full backup, the old one unchanged.
        clock.Now = new DateTimeOffset(2026, 4, 1, 18, 0, 0, TimeSpan.Zero);
        fs.AddFile(@"C:\src\Documents\new.txt", "new", lastWriteUtc: new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero));

        BackupRecord record = await engine.RunAsync(@"X:\Backups");

        Assert.Equal(BackupKind.Incremental, record.Kind);
        Assert.Equal(1, record.FilesCopiedCount);
        Assert.NotNull(fs.GetContent(@"X:\Backups\2026-04_INCR\Documents\new.txt"));
        Assert.Null(fs.GetContent(@"X:\Backups\2026-04_INCR\Documents\old.txt"));
    }

    [Fact]
    public async Task NewYear_PerformsFullCopyAgain()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 12, 1, 18, 0, 0, TimeSpan.Zero));
        var (engine, fs) = Build(clock);
        fs.AddFile(@"C:\src\Documents\a.txt", "one");
        await engine.RunAsync(@"X:\Backups");

        clock.Now = new DateTimeOffset(2027, 1, 1, 18, 0, 0, TimeSpan.Zero);
        BackupRecord record = await engine.RunAsync(@"X:\Backups");

        Assert.Equal(BackupKind.Full, record.Kind);
        Assert.NotNull(fs.GetContent(@"X:\Backups\2027_FULL\Documents\a.txt"));
    }

    [Fact]
    public async Task ExcludedFiles_AreNotCopiedAndNotErrors()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 1, 18, 0, 0, TimeSpan.Zero));
        var (engine, fs) = Build(clock);
        fs.AddFile(@"C:\src\Documents\keep.txt", "keep");
        fs.AddFile(@"C:\src\Documents\~$keep.docx", "lock");

        BackupRecord record = await engine.RunAsync(@"X:\Backups");

        Assert.Equal(1, record.FilesCopiedCount);
        Assert.Equal(1, record.ExcludedCount);
        Assert.Equal(BackupResultCode.Success, record.ResultCode);
    }

    [Fact]
    public async Task LockedFileWithoutVssHandler_IsSkipped_PartialSuccess()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 1, 18, 0, 0, TimeSpan.Zero));
        var (engine, fs) = Build(clock);
        fs.AddFile(@"C:\src\Documents\ok.txt", "ok");
        fs.AddFile(@"C:\src\Documents\locked.txt", "locked");
        fs.LockedPaths.Add(@"C:\src\Documents\locked.txt");

        BackupRecord record = await engine.RunAsync(@"X:\Backups");

        Assert.Equal(1, record.FilesCopiedCount);
        Assert.Equal(1, record.SkippedCount);
        Assert.Equal(BackupResultCode.PartialSuccess, record.ResultCode);
    }
}
