using OpenQA.Selenium.Appium.Windows;
using OpenQA.Selenium.Appium;

namespace WinBackup.Tests.E2E;

/// <summary>
/// Shared WinAppDriver session helper. Connects to a running WinAppDriver instance and launches the
/// WinBackup app. Tests use <see cref="IsAvailable"/> to skip gracefully when the driver or app is
/// not present (the app cannot be built without the WinUI/MSIX workload, so CI typically skips E2E).
/// </summary>
public static class AppSession
{
    private const string WinAppDriverUrl = "http://127.0.0.1:4723";

    /// <summary>Env var pointing at the built WinBackup app (full path to WinBackup.exe).</summary>
    public const string AppPathEnvVar = "WINBACKUP_APP_PATH";

    public static bool IsAvailable =>
        Environment.GetEnvironmentVariable(AppPathEnvVar) is { Length: > 0 } path && File.Exists(path);

    public static WindowsDriver Create()
    {
        string appPath = Environment.GetEnvironmentVariable(AppPathEnvVar)
            ?? throw new InvalidOperationException($"{AppPathEnvVar} is not set.");

        var options = new AppiumOptions();
        options.AddAdditionalAppiumOption("app", appPath);
        options.AddAdditionalAppiumOption("deviceName", "WindowsPC");

        return new WindowsDriver(new Uri(WinAppDriverUrl), options);
    }
}
