using System.IO;
using System.Windows;
using System.Windows.Threading;
using TremorScope.App.Services;
using TremorScope.App.ViewModels;

namespace TremorScope.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        try
        {
            var services = await AppServices.CreateAsync();
            var window = new MainWindow { DataContext = new MainViewModel(services) };
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            Log(ex);
            MessageBox.Show("起動できませんでした。\n\n" + ex.Message, "TremorScope", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        MessageBox.Show("予期しないエラーが起きました。作業は続けられます。\n\n" + e.Exception.Message, "TremorScope", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    /// <summary>エラーの記録（この PC の中だけ）</summary>
    private static void Log(Exception ex)
    {
        try
        {
            string dir = Path.Combine(AppServices.DataDirectory, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
