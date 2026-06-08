using WinBackup.Core.Browser;

namespace WinBackup.Tests.Unit.Fakes;

public sealed class FakeFileHistoryLocator : IFileHistoryLocator
{
    private readonly Dictionary<string, string?> _map = new(StringComparer.OrdinalIgnoreCase);

    public bool IsConnected { get; set; } = true;

    public void Map(string sourceFolder, string? versionsFolder) => _map[sourceFolder] = versionsFolder;

    public string? GetVersionsFolder(string sourceFolder) =>
        _map.TryGetValue(sourceFolder, out string? folder) ? folder : null;
}
