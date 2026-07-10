using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SurfaceMedic.App.Infrastructure;
using SurfaceMedic.App.ViewModels;

namespace SurfaceMedic.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppLaunchOptions _options;

    public MainWindow(MainViewModel viewModel, AppLaunchOptions options)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _options = options;
        DataContext = viewModel;
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        SourceInitialized += OnSourceInitialized;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        if (_options.Smoke)
        {
            await CaptureSmokeScreensAsync();
            Close();
            Application.Current.Shutdown(0);
        }
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            return;
        }

        DragMove();
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_OnClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnStateChanged(object? sender, EventArgs e)
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    private void ActivityLogTextBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        ActivityLogTextBox.ScrollToEnd();

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dark = _viewModel.IsDarkTheme ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var corners = 2;
        _ = DwmSetWindowAttribute(handle, 33, ref corners, sizeof(int));
    }

    private async Task CaptureSmokeScreensAsync()
    {
        var originalTheme = _viewModel.IsLightTheme ? "Light" : "Dark";
        Width = 1536;
        Height = 1024;
        UpdateLayout();
        await WaitForRenderAsync(250);

        var screenshotDirectory = Path.Combine(FindRepositoryRoot(), "screenshots");
        Directory.CreateDirectory(screenshotDirectory);

        await CapturePageAsync(AppPage.Overview, Path.Combine(screenshotDirectory, "app.png"), "Dark");
        _viewModel.PrepareThermalCaptureState();
        await CapturePageAsync(AppPage.Thermal, Path.Combine(screenshotDirectory, "thermal.png"), "Dark");
        _viewModel.ResetThermalCaptureState();
        await CapturePageAsync(AppPage.Power, Path.Combine(screenshotDirectory, "power.png"), "Dark");
        await CapturePageAsync(AppPage.Software, Path.Combine(screenshotDirectory, "software.png"), "Dark");
        await CapturePageAsync(AppPage.Maintenance, Path.Combine(screenshotDirectory, "maintenance.png"), "Dark");
        await CapturePageAsync(AppPage.Settings, Path.Combine(screenshotDirectory, "settings.png"), "Dark");
        await CapturePageAsync(AppPage.About, Path.Combine(screenshotDirectory, "about.png"), "Dark");
        await CapturePageAsync(AppPage.Overview, Path.Combine(screenshotDirectory, "app-light.png"), "Light");
        _viewModel.PrepareThermalCaptureState();
        await CapturePageAsync(AppPage.Thermal, Path.Combine(screenshotDirectory, "thermal-light.png"), "Light");
        _viewModel.ResetThermalCaptureState();
        await CapturePageAsync(AppPage.Power, Path.Combine(screenshotDirectory, "power-light.png"), "Light");
        await CapturePageAsync(AppPage.Software, Path.Combine(screenshotDirectory, "software-light.png"), "Light");
        await CapturePageAsync(AppPage.Maintenance, Path.Combine(screenshotDirectory, "maintenance-light.png"), "Light");
        await CapturePageAsync(AppPage.Settings, Path.Combine(screenshotDirectory, "settings-light.png"), "Light");
        await CapturePageAsync(AppPage.About, Path.Combine(screenshotDirectory, "about-light.png"), "Light");
        _viewModel.ApplyThemeForCapture(originalTheme);
        _viewModel.NavigateForCapture(AppPage.Overview);
    }

    private async Task CapturePageAsync(AppPage page, string path, string theme)
    {
        Hide();
        _viewModel.ApplyThemeForCapture(theme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark");
        _viewModel.NavigateForCapture(page);
        Show();
        await WaitForRenderAsync(60);
        _viewModel.ApplyThemeForCapture(theme);
        await WaitForRenderAsync(280);
        SaveScreenshot(path);
    }

    private async Task WaitForRenderAsync(int milliseconds)
    {
        await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.Render);
        await Task.Delay(milliseconds);
        await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.Render);
    }

    private void SaveScreenshot(string path)
    {
        var root = Content as Visual ?? this;
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SurfaceMedic.ps1")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SurfaceMedic.ps1")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the SurfaceMedic repository root for smoke screenshots.");
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);

}
