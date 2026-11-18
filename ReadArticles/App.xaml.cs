using System.Windows;
using ReadArticles.Infrastructure;

namespace ReadArticles;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
            AppLogger.Default.Critical("DispatcherUnhandledException", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLogger.Default.Critical("AppDomainUnhandledException", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            AppLogger.Default.Error("UnobservedTaskException", args.Exception);

        AppLogger.Default.Info("ApplicationStarted");
        Exit += (_, _) => AppLogger.Default.Info("ApplicationExited");

        base.OnStartup(e);
        var window = new MainWindow();
        window.Show();
        window.RestoreContent(Argument("--library"));

        string? Argument(string name)
        {
            int index = Array.IndexOf(e.Args, name);
            return index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : null;
        }
    }
}
