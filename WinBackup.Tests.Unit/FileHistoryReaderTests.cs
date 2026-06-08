using WinBackup.Core.Browser;
using WinBackup.Tests.Unit.Fakes;
using Xunit;

namespace WinBackup.Tests.Unit;

public sealed class FileHistoryReaderTests
{
    private const string Folder = @"F:\FileHistory\Data\Documents";

    private static FakeFileSystem WithVersions()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"F:\FileHistory\Data\Documents\Report (2026_01_10 09_00_00 UTC).docx", "r1");
        fs.AddFile(@"F:\FileHistory\Data\Documents\Report (2026_01_15 10_30_00 UTC).docx", "r2");
        fs.AddFile(@"F:\FileHistory\Data\Documents\Report (2026_02_01 08_00_00 UTC).docx", "r3");
        fs.AddFile(@"F:\FileHistory\Data\Documents\Notes (2026_01_12 12_00_00 UTC).txt", "n1");
        return fs;
    }

    private static DateTimeOffset Utc(int y, int m, int d, int h, int min) => new(y, m, d, h, min, 0, TimeSpan.Zero);

    [Fact]
    public void TryParseVersion_ValidName_ExtractsOriginalAndTimestamp()
    {
        bool ok = FileHistoryReader.TryParseVersion("Report (2026_01_15 10_30_00 UTC).docx",
            out string original, out DateTimeOffset ts);

        Assert.True(ok);
        Assert.Equal("Report.docx", original);
        Assert.Equal(Utc(2026, 1, 15, 10, 30), ts);
    }

    [Fact]
    public void TryParseVersion_NonVersionedName_ReturnsFalse()
    {
        Assert.False(FileHistoryReader.TryParseVersion("Report.docx", out _, out _));
    }

    [Fact]
    public void ListVersions_ReturnsAllVersionsOfFile_NewestFirst()
    {
        var sut = new FileHistoryReader(WithVersions());

        IReadOnlyList<FileHistoryReader.FhVersion> versions = sut.ListVersions(Folder, "Report.docx");

        Assert.Equal(3, versions.Count);
        Assert.Equal(Utc(2026, 2, 1, 8, 0), versions[0].Timestamp); // newest first
    }

    [Fact]
    public void GetVersionPath_ReturnsNewestAtOrBeforeTime()
    {
        var sut = new FileHistoryReader(WithVersions());

        string? path = sut.GetVersionPath(Folder, "Report.docx", Utc(2026, 1, 20, 0, 0));

        Assert.Equal(@"F:\FileHistory\Data\Documents\Report (2026_01_15 10_30_00 UTC).docx", path);
    }

    [Fact]
    public void GetContentsAt_ReturnsNewestVersionPerFile()
    {
        var sut = new FileHistoryReader(WithVersions());

        SnapshotContents contents = sut.GetContentsAt(Folder, Utc(2026, 1, 20, 0, 0));

        Assert.Equal(2, contents.Files.Count); // Report + Notes
        SnapshotFile report = contents.Files.Single(f => f.RelativePath == "Report.docx");
        Assert.Equal(@"F:\FileHistory\Data\Documents\Report (2026_01_15 10_30_00 UTC).docx", report.PhysicalPath);
    }

    [Fact]
    public void ListSnapshots_ReturnsDistinctTimestamps()
    {
        var sut = new FileHistoryReader(WithVersions());

        IReadOnlyList<BackupSnapshot> snaps = sut.ListSnapshots(Folder);

        Assert.Equal(4, snaps.Count); // 3 Report + 1 Notes, all distinct times
        Assert.All(snaps, s => Assert.Equal(SnapshotSource.FileHistory, s.Source));
        Assert.True(snaps[0].Timestamp >= snaps[^1].Timestamp); // newest first
    }
}
