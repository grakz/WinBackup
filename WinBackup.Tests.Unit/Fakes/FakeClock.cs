using WinBackup.Core.Abstractions;

namespace WinBackup.Tests.Unit.Fakes;

/// <summary>Settable <see cref="IClock"/> for deterministic time in tests.</summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }
}
