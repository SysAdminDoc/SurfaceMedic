using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using SurfaceMedic.App.Infrastructure;
using SurfaceMedic.App.ViewModels;
using SurfaceMedic.Core.Services;

namespace SurfaceMedic.App;

public partial class App : Application
{
    private AppLaunchOptions _options = new(false, false, false);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _options = AppLaunchOptions.Parse(e.Args);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        AppPaths.EnsureDirectories();
        RegisterExceptionHandlers();

        var isAdministrator = IsAdministrator();
        if (!isAdministrator && !_options.SkipElevation && TryRelaunchElevated(e.Args))
        {
            Shutdown(0);
            return;
        }

        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        ThemeManager.Apply(settings.Theme);

        var viewModel = new MainViewModel(new SurfaceMedicService(), isAdministrator, settingsService);
        var window = new MainWindow(viewModel, _options);
        MainWindow = window;

        if (_options.Background)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -32000;
            window.Top = 0;
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.Width = 1536;
            window.Height = 1024;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.Show();
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool TryRelaunchElevated(IReadOnlyList<string> arguments)
    {
        try
        {
            var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The application executable path is unavailable.");
            var commandLine = Environment.GetCommandLineArgs();
            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                Verb = "runas",
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory
            };

            if (Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add(commandLine[0]);
            }

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrashLog(args.ExceptionObject as Exception ?? new Exception("Unknown fatal error"));
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashLog(args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var path = WriteCrashLog(e.Exception);
        if (!_options.Background)
        {
            MessageBox.Show(
                $"SurfaceMedic encountered an unexpected problem. A crash report was saved to:\n\n{path}",
                "SurfaceMedic could not continue",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        e.Handled = true;
        Shutdown(1);
    }

    private static string WriteCrashLog(Exception exception)
    {
        AppPaths.EnsureDirectories();
        var path = Path.Combine(AppPaths.LogsDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var text = new StringBuilder()
            .AppendLine($"SurfaceMedic crash report - {DateTimeOffset.Now:O}")
            .AppendLine($"Runtime: {Environment.Version}")
            .AppendLine($"Windows: {Environment.OSVersion}")
            .AppendLine()
            .AppendLine(exception.ToString())
            .ToString();
        File.WriteAllText(path, text);
        return path;
    }
}
