using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Temhalo.App;

public sealed partial class MainWindow : Window
{
    private bool _closing, _closed;
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _accessibility = new();
    private readonly DispatcherTimer _themeMonitor = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _lastContrast;
    public MainWindow()
    {
        InitializeComponent();
        Dashboard.AttachWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        Dashboard.UpdateInstallerStarted += Close;
        AppWindow.Resize(InitialSize());
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "temhalo.ico"));
        UpdateTitleBar();
        // AccessibilitySettings event subscription can require CoreWindow in an unpackaged host.
        // A lightweight UI timer observes changes without relying on that unavailable event source.
        _themeMonitor.Tick += (_, _) => { if (_lastContrast != _accessibility.HighContrast) UpdateTitleBar(); };
        _themeMonitor.Start();
        AppWindow.Closing += Closing;
    }

    private void UpdateTitleBar()
    {
        _lastContrast = _accessibility.HighContrast;
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        var titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = _accessibility.HighContrast ? null : ((Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["TemhaloSurfaceBrush"]).Color;
        titleBar.ForegroundColor = _accessibility.HighContrast ? null : ((Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["TemhaloTextBrush"]).Color;
        titleBar.ButtonBackgroundColor = titleBar.BackgroundColor;
        titleBar.ButtonForegroundColor = titleBar.ForegroundColor;
        titleBar.InactiveBackgroundColor = titleBar.BackgroundColor;
        titleBar.InactiveForegroundColor = titleBar.ForegroundColor;
        titleBar.ButtonInactiveBackgroundColor = titleBar.BackgroundColor;
        titleBar.ButtonInactiveForegroundColor = titleBar.ForegroundColor;
    }

    private static SizeInt32 InitialSize()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--window-size");
        if (index >= 0 && index + 1 < args.Length)
        {
            string[] parts = args[index + 1].Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height) &&
                width is >= 320 and <= 3840 && height is >= 480 and <= 2160) return new SizeInt32(width, height);
        }
        return new SizeInt32(1280, 820);
    }

    private async void Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closed) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        await Dashboard.ShutdownAsync();
        _themeMonitor.Stop();
        _closed = true;
        Close();
    }
}
