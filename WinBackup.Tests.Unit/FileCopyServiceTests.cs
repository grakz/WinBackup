using System.Text;
using WinBackup.Core.Backup;
using WinBackup.Tests.Unit.Fakes;
using Xunit;

namespace WinBackup.Tests.Unit;

public sealed class FileCopyServiceTests
{
    private static readonly TimeSpan NoDelay = TimeSpan.Zero;

    private static FileCopyService Create(FakeFileSystem fs, int maxRetries = 3) =>
        new(fs, new FileFilterService(), maxRetries, NoDelay);

    [Fact]
    public async Task CopyAsync_HappyPath_CopiesAndReportsProgress()
    {
        var fs = new FakeFileSystem();
        byte[] content = Encoding.UTF8.GetBytes(new string('x', 5000));
        fs.AddFile(@"C:\src\a.txt", content);
        var progress = new List<FileCopyProgress>();

        FileCopyResult result = await Create(fs).CopyAsync(
            @"C:\src\a.txt", @"D:\dst\a.txt", new Progress<FileCopyProgress>(progress.Add));

        Assert.Equal(FileCopyOutcome.Copied, result.Outcome);
        Assert.Equal(content.Length, result.BytesCopied);
        Assert.Equal(content, fs.GetContent(@"D:\dst\a.txt"));
    }

    [Fact]
    public async Task CopyAsync_ExcludedFile_IsSkippedWithoutCopying()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\src\~$report.docx", "lock");

        FileCopyResult result = await Create(fs).CopyAsync(@"C:\src\~$report.docx", @"D:\dst\~$report.docx");

        Assert.Equal(FileCopyOutcome.Excluded, result.Outcome);
        Assert.Null(fs.GetContent(@"D:\dst\~$report.docx"));
    }

    [Fact]
    public async Task CopyAsync_SharingViolation_ReturnsVssFallbackWithoutRetrying()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\src\locked.bin", "data");
        fs.LockedPaths.Add(@"C:\src\locked.bin");

        FileCopyResult result = await Create(fs, maxRetries: 3).CopyAsync(@"C:\src\locked.bin", @"D:\dst\locked.bin");

        Assert.Equal(FileCopyOutcome.RequiresVssFallback, result.Outcome);
        Assert.Equal(1, fs.ReadOpenCounts[@"C:\src\locked.bin"]); // no retry
    }

    [Fact]
    public async Task CopyAsync_TransientError_RetriesThenSucceeds()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\src\b.txt", "hello");
        fs.TransientReadFailures[@"C:\src\b.txt"] = 2; // fail twice, succeed on 3rd

        FileCopyResult result = await Create(fs, maxRetries: 3).CopyAsync(@"C:\src\b.txt", @"D:\dst\b.txt");

        Assert.Equal(FileCopyOutcome.Copied, result.Outcome);
        Assert.Equal(3, fs.ReadOpenCounts[@"C:\src\b.txt"]);
    }

    [Fact]
    public async Task CopyAsync_PersistentNonSharingError_GivesUpAndThrows()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\src\c.txt", "hello");
        fs.TransientReadFailures[@"C:\src\c.txt"] = 99; // always fail

        FileCopyService sut = Create(fs, maxRetries: 2);

        await Assert.ThrowsAsync<IOException>(() => sut.CopyAsync(@"C:\src\c.txt", @"D:\dst\c.txt"));
        Assert.Equal(3, fs.ReadOpenCounts[@"C:\src\c.txt"]); // initial + 2 retries
    }

    [Fact]
    public async Task CopyAsync_HashMismatch_Throws()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\src\d.txt", "important");
        fs.CorruptOnWrite.Add(@"D:\dst\d.txt"); // destination silently corrupted

        FileCopyService sut = Create(fs);

        await Assert.ThrowsAsync<HashMismatchException>(() => sut.CopyAsync(@"C:\src\d.txt", @"D:\dst\d.txt"));
    }

    [Fact]
    public async Task CopyAsync_CancelledToken_ThrowsOperationCanceled()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\src\e.txt", "data");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        FileCopyService sut = Create(fs);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.CopyAsync(@"C:\src\e.txt", @"D:\dst\e.txt", null, cts.Token));
    }
}
