using Microsoft.UI.Xaml;

namespace WinBackup.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        // The shell stays hidden; the app lives in the system tray (wired in later phases).
        _window.Activate();
    }
}
