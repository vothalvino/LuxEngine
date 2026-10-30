using System.Diagnostics;
using LuxEngine.App.Services;
using Microsoft.UI.Xaml;

namespace LuxEngine.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        // Failed background tasks are logged instead of tearing the process down later.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogError(e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Writes an error to errors.log next to the autosave; never throws.</summary>
    public static void LogError(Exception ex)
    {
        Trace.WriteLine(ex);
        try
        {
            File.AppendAllText(Path.Combine(AppPaths.DataFolder, "errors.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}\n\n");
        }
        catch (Exception logEx) when (logEx is IOException or UnauthorizedAccessException)
        {
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.Initialize();
        var window = new MainWindow();
        AppServices.MainWindow = window;
        window.Activate();

        // A .luxshow file double-clicked in Explorer arrives as the first argument.
        var file = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => a.EndsWith(".luxshow", StringComparison.OrdinalIgnoreCase));
        window.StartUp(file);
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Keep the lights running: save a recovery copy and report the problem instead of crashing.
        AppServices.Document.Autosave();
        e.Handled = true;
        LogError(e.Exception);
        _ = Helpers.DialogHelper.ShowMessageAsync("Something went wrong",
            $"LuxEngine hit an unexpected error but kept running. Your show was saved for recovery.\n\n{e.Message}");
    }
}
