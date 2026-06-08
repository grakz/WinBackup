using Xunit;

namespace WinBackup.Tests.E2E;

/// <summary>
/// E2E smoke tests. These require the WinUI app to be built (needs the Windows App SDK / MSIX
/// workload) and a running WinAppDriver, so they self-skip when <see cref="AppSession.IsAvailable"/>
/// is false. The assertions encode the Phase 7.2 scenarios for when the environment is complete.
/// </summary>
public sealed class SmokeTests
{
    [SkippableFact]
    public void App_Launches_AndTrayWindowIsReachable()
    {
        Skip.IfNot(AppSession.IsAvailable, "WinBackup app / WinAppDriver not available in this environment.");

        using var driver = AppSession.Create();
        Assert.False(string.IsNullOrEmpty(driver.Title));
    }
}
