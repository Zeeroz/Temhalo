using Microsoft.UI.Xaml;

namespace Temhalo.App;

public partial class App : Application
{
    // Keep the native window alive; starting the media session remains an explicit action.
    private Window? _window;

    public App()
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        UnhandledException += (_, error) => WriteStartupDiagnostic(error.Exception);
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex) { WriteStartupDiagnostic(ex); throw; }
    }

    private static void WriteStartupDiagnostic(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Services.AppStorage.DataDirectory);
            File.WriteAllText(Path.Combine(Services.AppStorage.DataDirectory, "startup-error.log"),
                Temhalo.Infrastructure.Windows.Logging.SecretRedactor.Shared.Redact(exception.ToString()));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
