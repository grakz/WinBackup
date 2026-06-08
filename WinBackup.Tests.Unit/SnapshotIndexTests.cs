using WinBackup.Core.Browser;
using WinBackup.Tests.Unit.Fakes;
using Xunit;

namespace WinBackup.Tests.Unit;

public sealed class SnapshotIndexTests
{
    private const string Source = @"C:\Users\me\Documents";
    private const string SsdRoot = @"X:\Backups";
    private const string FhFolder = @"F:\FileHistory\Data\Documents";

    private static DateTimeOffset At(int y, int m, int d) => new(y, m, d, 12, 0, 0, TimeSpan.Zero);

    private static FakeFileSystem WithBothSources()
    {
        var fs = new FakeFileSystem();
        // SSD: full in 2026 with one file.
        fs.AddFile(@"X:\Backups\2026_FULL\Documents\old.txt", "ssd-old");
        // FH: a recent version of a file.
        fs.AddFile(@"F:\FileHistory\Data\Documents\Report (2026_05_10 10_00_00 UTC).docx", "fh-report");
        return fs;
    }

    private static SnapshotIndex Build(FakeFileSystem fs, FakeFileHistoryLocator locator, string? ssdRoot)
    {
        return new SnapshotIndex(
            fs,
            new SsdSnapshotReader(fs),
            new FileHistoryReader(fs),
            locator,
            () => ssdRoot);
    }

    [Fact]
    public void GetContentsAt_FhHasVersion_RoutesToFileHistory()
    {
        var fs = WithBothSources();
        var locator = new FakeFileHistoryLocator { IsConnected = true };
        locator.Map(Source, FhFolder);
        var sut = Build(fs, locator, SsdRoot);

        SnapshotContents contents = sut.GetContentsAt(Source, At(2026, 5, 20));

        Assert.True(contents.IsAvailable);
        Assert.All(contents.Files, f => Assert.Equal(SnapshotSource.FileHistory, f.Source));
        Assert.Contains(contents.Files, f => f.RelativePath == "Report.docx");
    }

    [Fact]
    public void GetContentsAt_FhUnavailable_FallsBackToSsd()
    {
        var fs = WithBothSources();
        var locator = new FakeFileHistoryLocator { IsConnected = false };
        var sut = Build(fs, locator, SsdRoot);

        SnapshotContents contents = sut.GetContentsAt(Source, At(2026, 6, 1));

        Assert.True(contents.IsAvailable);
        Assert.All(contents.Files, f => Assert.Equal(SnapshotSource.SsdFull, f.Source));
        Assert.Contains(contents.Files, f => f.RelativePath == "old.txt");
    }

    [Fact]
    public void GetContentsAt_NeitherAvailable_ReturnsUnavailableReason()
    {
        var fs = WithBothSources();
        var locator = new FakeFileHistoryLocator { IsConnected = false };
        var sut = Build(fs, locator, ssdRoot: null); // SSD not connected

        SnapshotContents contents = sut.GetContentsAt(Source, At(2026, 6, 1));

        Assert.False(contents.IsAvailable);
        Assert.NotNull(contents.UnavailableReason);
    }

    [Fact]
    public void GetTimeline_MergesBothSources_NewestFirst()
    {
        var fs = WithBothSources();
        var locator = new FakeFileHistoryLocator { IsConnected = true };
        locator.Map(Source, FhFolder);
        var sut = Build(fs, locator, SsdRoot);

        IReadOnlyList<BackupSnapshot> timeline = sut.GetTimeline(Source);

        Assert.Equal(2, timeline.Count); // 1 SSD full + 1 FH version
        Assert.Contains(timeline, s => s.Source == SnapshotSource.SsdFull);
        Assert.Contains(timeline, s => s.Source == SnapshotSource.FileHistory);
        // Sorted descending by timestamp.
        Assert.True(timeline[0].Timestamp >= timeline[1].Timestamp);
    }
}
