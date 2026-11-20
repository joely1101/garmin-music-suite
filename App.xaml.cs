using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace GarminMusicStudio;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("DispatcherUnhandledException", e.Exception);
        MessageBox.Show($"應用程式發生錯誤: {e.Exception.Message}\n\n已記錄至 crash.log", "非預期錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true; // Prevent the app from abruptly terminating!
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash("UnhandledException", ex);
        }
    }

    private static void LogCrash(string type, Exception ex)
    {
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{type}] {ex}\n\n";
            File.AppendAllText(logPath, msg);
        }
        catch { }
    }
}
