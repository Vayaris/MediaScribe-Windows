using System.Windows;
using System.Windows.Threading;
using MediaScribeRecorder.Services;

namespace MediaScribeRecorder;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        base.OnStartup(e);
        new MainWindow().Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryLogCrash(e.Exception);
        MessageBox.Show(e.Exception.Message, "MediaScribe Recorder", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            TryLogCrash(exception);
        }
    }

    private static void TryLogCrash(Exception exception)
    {
        try
        {
            var paths = new PortableAppPaths();
            var log = new LogService(paths);
            log.Error(exception, "Unhandled application error.");
        }
        catch
        {
        }
    }
}
