using WinBackup.Core.OneDrive;

namespace WinBackup.Tests.Unit.Fakes;

public sealed class FakePlaceholderController : IPlaceholderController
{
    public List<string> Dehydrated { get; } = new();
    public bool Throw { get; set; }

    public void Dehydrate(string path)
    {
        if (Throw)
        {
            throw new IOException("dehydration failed");
        }

        Dehydrated.Add(path);
    }
}
