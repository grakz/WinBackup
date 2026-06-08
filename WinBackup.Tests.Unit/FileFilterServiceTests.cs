using WinBackup.Core.Backup;
using Xunit;

namespace WinBackup.Tests.Unit;

public sealed class FileFilterServiceTests
{
    [Theory]
    [InlineData(@"C:\docs\~$report.docx")]   // Office lock file
    [InlineData(@"C:\docs\~$budget.xlsx")]
    [InlineData(@"C:\tmp\scratch.tmp")]
    [InlineData(@"C:\docs\page.~wbk")]        // editor temp variant
    [InlineData(@"C:\docs\desktop.ini")]
    [InlineData(@"C:\docs\Thumbs.db")]        // case-insensitive
    [InlineData(@"C:\docs\ehthumbs.db")]
    public void ShouldExclude_BuiltInPatterns_AreExcluded(string path)
    {
        var sut = new FileFilterService();
        Assert.True(sut.ShouldExclude(path));
    }

    [Theory]
    [InlineData(@"C:\docs\document.docx")]
    [InlineData(@"C:\photos\img.jpg")]
    [InlineData(@"C:\code\Program.cs")]
    [InlineData(@"C:\docs\notes.txt")]
    public void ShouldExclude_NormalFiles_AreNotExcluded(string path)
    {
        var sut = new FileFilterService();
        Assert.False(sut.ShouldExclude(path));
    }

    [Fact]
    public void ShouldExclude_UserPattern_IsApplied()
    {
        var sut = new FileFilterService(new[] { "*.bak" });

        Assert.True(sut.ShouldExclude(@"C:\docs\archive.bak"));
        Assert.False(sut.ShouldExclude(@"C:\docs\archive.zip"));
    }

    [Fact]
    public void ShouldExclude_EmptyOrNull_IsFalse()
    {
        var sut = new FileFilterService();
        Assert.False(sut.ShouldExclude(""));
    }

    [Fact]
    public void ShouldExclude_BlankUserPatterns_AreIgnored()
    {
        // Whitespace patterns must not accidentally match everything.
        var sut = new FileFilterService(new[] { "  ", "" });
        Assert.False(sut.ShouldExclude(@"C:\docs\document.docx"));
    }
}
