namespace WinBackup.Core.Abstractions;

/// <summary>Time seam so scheduling logic can be driven by a fake clock in tests.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

/// <summary>Real wall-clock implementation of <see cref="IClock"/>.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
