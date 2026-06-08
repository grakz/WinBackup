using WinBackup.Core.Browser;
using WinBackup.Tests.Unit.Fakes;
using Xunit;

namespace WinBackup.Tests.Unit;

public sealed class SsdSnapshotReaderTests
{
    private const string Root = @"X:\Backups";

    private static FakeFileSystem WithLayout()
    {
        var fs = new FakeFileSystem();
        // Full baseline (2026), plus February and March incrementals.
        fs.AddFile(@"X:\Backups\2026_FULL\Documents\a.txt", "a-v1");
        fs.AddFile(@"X:\Backups\2026_FULL\Documents\b.txt", "b-v1");
        fs.AddFile(@"X:\Backups\2026-02_INCR\Documents\a.txt", "a-v2"); // modified
        fs.AddFile(@"X:\Backups\2026-03_INCR\Documents\c.txt", "c-v1"); // new
        return fs;
    }

    private static DateTimeOffset At(int y, int m, int d) => new(y, m, d, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoSnapshots_EmptyList()
    {
        var fs = new FakeFileSystem();
        fs.CreateDirectory(Root);
        var sut = new SsdSnapshotReader(fs);

        Assert.Empty(sut.ListSnapshots(Root, "Documents"));
    }

    [Fact]
    public void ListSnapshots_ReturnsFullAndIncrementsInOrder()
    {
        var sut = new SsdSnapshotReader(WithLayout());

        IReadOnlyList<BackupSnapshot> snaps = sut.ListSnapshots(Root, "Documents");

        Assert.Equal(3, snaps.Count);
        Assert.Equal(SnapshotSource.SsdFull, snaps[0].Source);
        Assert.Equal(SnapshotSource.SsdIncremental, snaps[1].Source);
        Assert.Equal(SnapshotSource.SsdIncremental, snaps[2].Source);
    }

    [Fact]
    public void GetContentsAt_BeforeIncrements_ReturnsFullOnly()
    {
        var sut = new SsdSnapshotReader(WithLayout());

        SnapshotContents contents = sut.GetContentsAt(Root, "Documents", At(2026, 1, 15));

        Assert.Equal(2, contents.Files.Count);
        SnapshotFile a = contents.Files.Single(f => f.RelativePath == "a.txt");
        Assert.Equal(@"X:\Backups\2026_FULL\Documents\a.txt", a.PhysicalPath); // v1
    }

    [Fact]
    public void GetContentsAt_BetweenIncrements_AppliesNewerVersionWins()
    {
        var sut = new SsdSnapshotReader(WithLayout());

        SnapshotContents contents = sut.GetContentsAt(Root, "Documents", At(2026, 2, 15));

        Assert.Equal(2, contents.Files.Count); // a (v2), b ; c not yet
        SnapshotFile a = contents.Files.Single(f => f.RelativePath == "a.txt");
        Assert.Equal(@"X:\Backups\2026-02_INCR\Documents\a.txt", a.PhysicalPath); // v2 wins
        Assert.DoesNotContain(contents.Files, f => f.RelativePath == "c.txt");
    }

    [Fact]
    public void GetContentsAt_AfterAllIncrements_IncludesEverything()
    {
        var sut = new SsdSnapshotReader(WithLayout());

        SnapshotContents contents = sut.GetContentsAt(Root, "Documents", At(2026, 3, 15));

        Assert.Equal(3, contents.Files.Count);
        Assert.Contains(contents.Files, f => f.RelativePath == "c.txt");
    }

    [Fact]
    public void GetContentsAt_SsdNotConnected_ReturnsUnavailable()
    {
        var fs = new FakeFileSystem(); // root never created → "disconnected"
        var sut = new SsdSnapshotReader(fs);

        SnapshotContents contents = sut.GetContentsAt(Root, "Documents", At(2026, 3, 15));

        Assert.False(contents.IsAvailable);
        Assert.NotNull(contents.UnavailableReason);
    }
}
