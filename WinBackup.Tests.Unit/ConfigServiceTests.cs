using WinBackup.Core.Config;
using Xunit;

namespace WinBackup.Tests.Unit;

public sealed class ConfigServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly ConfigService _sut = new();

    public ConfigServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "winbackup-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        BackupConfig config = _sut.Load(Path.Combine(_dir, "does-not-exist.json"));

        Assert.NotNull(config);
        Assert.Empty(config.SourceFolders);
        Assert.Equal("BACKUP_SSD", config.Ssd.VolumeLabel);
        Assert.Equal(2, config.Proton.LookbackBackups);
        Assert.Equal("18:00", config.Schedule.SsdTime);
    }

    [Fact]
    public void Load_MalformedJson_ReturnsDefaultsWithoutThrowing()
    {
        string path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, "{ this is not valid json ");

        BackupConfig config = _sut.Load(path);

        Assert.NotNull(config);
        Assert.Equal("BACKUP_SSD", config.Ssd.VolumeLabel);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        string path = Path.Combine(_dir, "config.json");
        var original = new BackupConfig
        {
            SourceFolders = { @"C:\Users\me\Documents", @"C:\Users\me\Pictures" },
            ExcludePatterns = { "*.bak" },
            LogDir = @"C:\ProgramData\WinBackup\logs",
            Ssd =
            {
                VolumeLabel = "MY_SSD",
                DiskSerial = "ABC123",
                BackupSubdir = "Snaps",
                DismountAfterBackup = false,
                ConnectWaitMinutes = 45,
            },
            Proton =
            {
                SyncFolder = @"C:\Users\me\Proton Drive\Backups",
                LookbackBackups = 3,
            },
            Schedule =
            {
                SsdDayOfMonth = 5,
                SsdTime = "19:30",
                ProtonTime = "03:15",
            },
        };

        _sut.Save(path, original);
        BackupConfig loaded = _sut.Load(path);

        Assert.Equal(original.SourceFolders, loaded.SourceFolders);
        Assert.Equal(original.ExcludePatterns, loaded.ExcludePatterns);
        Assert.Equal(original.LogDir, loaded.LogDir);
        Assert.Equal("MY_SSD", loaded.Ssd.VolumeLabel);
        Assert.Equal("ABC123", loaded.Ssd.DiskSerial);
        Assert.Equal("Snaps", loaded.Ssd.BackupSubdir);
        Assert.False(loaded.Ssd.DismountAfterBackup);
        Assert.Equal(45, loaded.Ssd.ConnectWaitMinutes);
        Assert.Equal(@"C:\Users\me\Proton Drive\Backups", loaded.Proton.SyncFolder);
        Assert.Equal(3, loaded.Proton.LookbackBackups);
        Assert.Equal(5, loaded.Schedule.SsdDayOfMonth);
        Assert.Equal("19:30", loaded.Schedule.SsdTime);
        Assert.Equal("03:15", loaded.Schedule.ProtonTime);
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        string path = Path.Combine(_dir, "nested", "deeper", "config.json");

        _sut.Save(path, new BackupConfig());

        Assert.True(File.Exists(path));
    }
}
