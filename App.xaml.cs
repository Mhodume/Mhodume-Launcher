using System.IO;
using System.Windows;

namespace Mhodume;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Mhodume", "launcher-error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // An unhandled WPF exception closes the window with nothing to show, so
        // record it and say where.
        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            MessageBox.Show(
                "Something went wrong:\n\n" + args.Exception.Message +
                "\n\nDetails written to:\n" + LogPath,
                "Mhodume", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Log(ex);
        };

        base.OnStartup(e);
    }

    private static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath,
                $"{DateTime.Now:s}  {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }
}
