using WinBackup.Core.Abstractions;
using WinBackup.Core.Backup;
using WinBackup.Core.State;
using WinBackup.Tests.Unit.Fakes;
using Xunit;

namespace WinBackup.Tests.Unit;

/// <summary>Phase 3.2: cloud-only files are re-dehydrated after a verified copy.</summary>
public sealed class FileSetCopierDehydrateTests
{
    private static readonly FileAttributes CloudOnly =
        FileAttributes.Normal | (FileAttributes)FileItem.RecallOnDataAccess;

    private static CopyJob Job(FakeFileSystem fs, string source, string dest) =>
        new(fs.GetItem(source), dest);

    [Fact]
    public async Task CloudOnlyFile_IsDehydratedAfterCopy_LocalFileIsNot()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\OneDrive\cloud.bin", "data", attributes: CloudOnly);
        fs.AddFile(@"C:\OneDrive\local.txt", "data", attributes: FileAttributes.Normal);
        var placeholders = new FakePlaceholderController();

        var copier = new FileSetCopier(
            new FileCopyService(fs, new FileFilterService(), 0, TimeSpan.Zero),
            lockedHandler: null,
            placeholders: placeholders);

        var jobs = new[]
        {
            Job(fs, @"C:\OneDrive\cloud.bin", @"X:\b\cloud.bin"),
            Job(fs, @"C:\OneDrive\local.txt", @"X:\b\local.txt"),
        };

        CopyTally tally = await copier.CopyAllAsync(jobs, "test", null, default);

        Assert.Equal(2, tally.Copied);
        Assert.Equal(new[] { @"C:\OneDrive\cloud.bin" }, placeholders.Dehydrated);
        Assert.Equal(BackupResultCode.Success, tally.ToResultCode());
    }

    [Fact]
    public async Task DehydrationFailure_KeepsCopy_ButMarksPartialSuccess()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\OneDrive\cloud.bin", "data", attributes: CloudOnly);
        var placeholders = new FakePlaceholderController { Throw = true };

        var copier = new FileSetCopier(
            new FileCopyService(fs, new FileFilterService(), 0, TimeSpan.Zero),
            lockedHandler: null,
            placeholders: placeholders);

        var jobs = new[] { Job(fs, @"C:\OneDrive\cloud.bin", @"X:\b\cloud.bin") };

        CopyTally tally = await copier.CopyAllAsync(jobs, "test", null, default);

        Assert.Equal(1, tally.Copied);                               // copy still succeeded
        Assert.NotNull(fs.GetContent(@"X:\b\cloud.bin"));
        Assert.Equal(BackupResultCode.PartialSuccess, tally.ToResultCode());
    }
}
