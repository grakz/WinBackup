using WinBackup.Core.Abstractions;
using WinBackup.Core.OneDrive;
using WinBackup.Tests.Unit.Fakes;
using Xunit;

namespace WinBackup.Tests.Unit;

public sealed class OneDriveEnumeratorTests
{
    private static readonly FileAttributes CloudOnly =
        FileAttributes.Normal | (FileAttributes)FileItem.RecallOnDataAccess;

    [Fact]
    public void Enumerate_ClassifiesLocalAndCloudOnlyFiles()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\OneDrive\local.txt", "hello", attributes: FileAttributes.Normal);
        fs.AddFile(@"C:\OneDrive\cloud.bin", new byte[1234], attributes: CloudOnly);

        var sut = new OneDriveFileEnumerator(fs);
        Dictionary<string, OneDriveEntry> byName = sut.Enumerate(@"C:\OneDrive")
            .ToDictionary(e => Path.GetFileName(e.Path));

        Assert.False(byName["local.txt"].IsCloudOnly);
        Assert.Equal(5, byName["local.txt"].SizeBytes);

        Assert.True(byName["cloud.bin"].IsCloudOnly);
        Assert.Equal(1234, byName["cloud.bin"].SizeBytes);
    }

    [Fact]
    public void FileItem_IsCloudOnly_DetectsRecallAttribute()
    {
        var local = new FileItem(@"C:\a.txt", 1, DateTimeOffset.UtcNow, FileAttributes.Normal);
        var cloud = new FileItem(@"C:\b.txt", 1, DateTimeOffset.UtcNow, CloudOnly);

        Assert.False(local.IsCloudOnly);
        Assert.True(cloud.IsCloudOnly);
    }
}
