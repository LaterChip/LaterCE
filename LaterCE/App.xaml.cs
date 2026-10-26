using LaterCE.Services;
using Microsoft.UI.Xaml;

namespace LaterCE;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Error("未处理异常: " + e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppPaths.Ensure();
        ConfigService.Load();
        JavaFinder.Initialize();

        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
