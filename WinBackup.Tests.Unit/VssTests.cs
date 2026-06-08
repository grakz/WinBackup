using WinBackup.Core.Backup;
using WinBackup.Core.Pipes;
using WinBackup.Core.Volume;
using WinBackup.Tests.Unit.Fakes;
using Xunit;

namespace WinBackup.Tests.Unit;

public sealed class ShadowPathTests
{
    [Fact]
    public void Map_StripsDriveRoot_AppendsToDevicePath()
    {
        string mapped = ShadowPath.Map(
            @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1",
            @"C:\Users\me\a.txt");

        Assert.Equal(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1\Users\me\a.txt", mapped);
    }
}

public sealed class VssCoordinatorTests
{
    [Fact]
    public async Task GetShadowRoot_CachesPerVolume_OneSnapshotPerVolume()
    {
        var client = new FakeElevatedHelperClient { SnapshotRoot = @"\\?\GLOBALROOT\Device\Shadow1" };
        var sut = new VssCoordinator(client);

        string? first = await sut.GetShadowRootAsync(@"C:\");
        string? second = await sut.GetShadowRootAsync(@"C:\Users\me"); // same volume

        Assert.Equal(@"\\?\GLOBALROOT\Device\Shadow1", first);
        Assert.Equal(first, second);
        Assert.Equal(1, client.SnapshotCalls); // snapshot reused
    }

    [Fact]
    public async Task GetShadowRoot_SnapshotFails_ReturnsNull()
    {
        var client = new FakeElevatedHelperClient { SnapshotRoot = null };
        var sut = new VssCoordinator(client);

        Assert.Null(await sut.GetShadowRootAsync(@"C:\"));
    }

    [Fact]
    public async Task DeleteAll_SendsDeleteForEachSnapshot()
    {
        var client = new FakeElevatedHelperClient();
        var sut = new VssCoordinator(client);
        await sut.GetShadowRootAsync(@"C:\");
        await sut.GetShadowRootAsync(@"D:\");

        await sut.DeleteAllAsync();

        Assert.Equal(2, client.Requests.Count(r => r.Command == HelperCommand.VssDeleteSnapshot));
    }
}

public sealed class VssLockedFileHandlerTests
{
    [Fact]
    public async Task TryCopyLocked_CopiesFromShadow_WhenSnapshotAvailable()
    {
        var fs = new FakeFileSystem();
        // The locked original lives at C:\Users\me\locked.txt; the readable copy is on the shadow.
        fs.AddFile(@"\\?\GLOBALROOT\Device\Shadow1\Users\me\locked.txt", "recovered");

        var coordinator = new FakeVssCoordinator { ShadowRoot = @"\\?\GLOBALROOT\Device\Shadow1" };
        var copy = new FileCopyService(fs, new FileFilterService(), 0, TimeSpan.Zero);
        var sut = new VssLockedFileHandler(coordinator, copy);

        bool ok = await sut.TryCopyLockedAsync(@"C:\Users\me\locked.txt", @"X:\b\locked.txt", null, default);

        Assert.True(ok);
        Assert.Equal("recovered", System.Text.Encoding.UTF8.GetString(fs.GetContent(@"X:\b\locked.txt")!));
    }

    [Fact]
    public async Task TryCopyLocked_NoSnapshot_ReturnsFalse()
    {
        var coordinator = new FakeVssCoordinator { ShadowRoot = null };
        var copy = new FileCopyService(new FakeFileSystem(), new FileFilterService(), 0, TimeSpan.Zero);
        var sut = new VssLockedFileHandler(coordinator, copy);

        bool ok = await sut.TryCopyLockedAsync(@"C:\Users\me\locked.txt", @"X:\b\locked.txt", null, default);

        Assert.False(ok);
    }

    [Fact]
    public async Task LockedFile_ThroughFileSetCopier_CountsAsVssFallback()
    {
        // End-to-end: a locked file routes through the handler and is tallied as a VSS copy.
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\src\locked.txt", "live");
        fs.LockedPaths.Add(@"C:\src\locked.txt");
        fs.AddFile(@"\\?\GLOBALROOT\Device\Shadow1\src\locked.txt", "snapshot-copy");

        var coordinator = new FakeVssCoordinator { ShadowRoot = @"\\?\GLOBALROOT\Device\Shadow1" };
        var copy = new FileCopyService(fs, new FileFilterService(), 0, TimeSpan.Zero);
        var copier = new FileSetCopier(copy, new VssLockedFileHandler(coordinator, copy));

        var jobs = new[] { new CopyJob(fs.GetItem(@"C:\src\locked.txt"), @"X:\b\locked.txt") };
        CopyTally tally = await copier.CopyAllAsync(jobs, "test", null, default);

        Assert.Equal(1, tally.Vss);
        Assert.Equal(0, tally.Skipped);
        Assert.Equal("snapshot-copy", System.Text.Encoding.UTF8.GetString(fs.GetContent(@"X:\b\locked.txt")!));
    }
}
