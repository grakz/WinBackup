using WinBackup.Core.Backup;
using WinBackup.Core.Config;
using WinBackup.Core.State;
using WinBackup.Tests.Unit.Fakes;
using Xunit;

namespace WinBackup.Tests.Unit;

public sealed class ProtonBackupEngineTests : IDisposable
{
    private readonly string _dir;
    private readonly string _statePath;
    private readonly StateService _state = new();

    public ProtonBackupEngineTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "winbackup-proton-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _statePath = Path.Combine(_dir, "state.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private ProtonBackupEngine Build(FakeClock clock, FakeFileSystem fs)
    {
        var config = new BackupConfig
        {
            SourceFolders = { @"C:\src\Documents" },
            Proton = { SyncFolder = @"P:\ProtonDrive\Backups", LookbackBackups = 2 },
        };
        var copier = new FileSetCopier(new FileCopyService(fs, new FileFilterService(), maxRetries: 0, retryDelay: TimeSpan.Zero));
        return new ProtonBackupEngine(fs, copier, _state, _statePath, clock, config);
    }

    private void SeedSuccessfulSsd(DateTimeOffset startedAt)
    {
        _state.AddRecord(_statePath, new BackupRecord
        {
            Target = BackupTarget.Ssd,
            Kind = BackupKind.Full,
            StartedAt = startedAt,
            CompletedAt = startedAt.AddMinutes(10),
            ResultCode = BackupResultCode.Success,
        });
    }

    [Fact]
    public async Task NoChangesSinceCutoff_CreatesNoFolder()
    {
        var ssdTime = new DateTimeOffset(2026, 5, 1, 18, 0, 0, TimeSpan.Zero);
        SeedSuccessfulSsd(ssdTime);

        var clock = new FakeClock(new DateTimeOffset(2026, 5, 10, 2, 0, 0, TimeSpan.Zero));
        var fs = new FakeFileSystem();
        // Only an old file, modified before the SSD cutoff → nothing to copy.
        fs.AddFile(@"C:\src\Documents\old.txt", "old", lastWriteUtc: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

        BackupRecord record = await Build(clock, fs).RunAsync();

        Assert.Equal(0, record.FilesCopiedCount);
        Assert.False(fs.DirectoryExists(@"P:\ProtonDrive\Backups\2026-05-10"));
    }

    [Fact]
    public async Task FilesChanged_CopiesIntoDatedFolder()
    {
        var ssdTime = new DateTimeOffset(2026, 5, 1, 18, 0, 0, TimeSpan.Zero);
        SeedSuccessfulSsd(ssdTime);

        var clock = new FakeClock(new DateTimeOffset(2026, 5, 10, 2, 0, 0, TimeSpan.Zero));
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\src\Documents\fresh.txt", "fresh", lastWriteUtc: new DateTimeOffset(2026, 5, 9, 0, 0, 0, TimeSpan.Zero));

        BackupRecord record = await Build(clock, fs).RunAsync();

        Assert.Equal(1, record.FilesCopiedCount);
        Assert.NotNull(fs.GetContent(@"P:\ProtonDrive\Backups\2026-05-10\Documents\fresh.txt"));
    }

    [Fact]
    public async Task NoSsdHistory_CutoffNull_CopiesAllFiles()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 10, 2, 0, 0, TimeSpan.Zero));
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\src\Documents\a.txt", "a", lastWriteUtc: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        fs.AddFile(@"C:\src\Documents\b.txt", "b", lastWriteUtc: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

        BackupRecord record = await Build(clock, fs).RunAsync();

        Assert.Equal(2, record.FilesCopiedCount);
    }

    [Fact]
    public async Task ExcludedFiles_AreNotCopied()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 10, 2, 0, 0, TimeSpan.Zero));
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\src\Documents\keep.txt", "k", lastWriteUtc: clock.Now);
        fs.AddFile(@"C:\src\Documents\Thumbs.db", "x", lastWriteUtc: clock.Now);

        BackupRecord record = await Build(clock, fs).RunAsync();

        Assert.Equal(1, record.FilesCopiedCount);
        Assert.Equal(1, record.ExcludedCount);
    }

    [Fact]
    public async Task SkipOnError_LockedFileWithoutVss_PartialSuccess()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 10, 2, 0, 0, TimeSpan.Zero));
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\src\Documents\ok.txt", "ok", lastWriteUtc: clock.Now);
        fs.AddFile(@"C:\src\Documents\locked.txt", "l", lastWriteUtc: clock.Now);
        fs.LockedPaths.Add(@"C:\src\Documents\locked.txt");

        BackupRecord record = await Build(clock, fs).RunAsync();

        Assert.Equal(1, record.FilesCopiedCount);
        Assert.Equal(1, record.SkippedCount);
        Assert.Equal(BackupResultCode.PartialSuccess, record.ResultCode);
    }
}
