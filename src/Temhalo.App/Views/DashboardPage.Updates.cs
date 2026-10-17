using System.ComponentModel;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Temhalo.App.Services;
using Temhalo.Infrastructure.Windows.Deployment;

namespace Temhalo.App.Views;

public sealed partial class DashboardPage
{
    private readonly HttpClient _updateHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly CancellationTokenSource _updateLifetime = new();
    private CancellationTokenSource? _updateCancellation;
    private Task? _updateTask;
    private UpdateRelease? _availableUpdate;
    private DownloadedUpdate? _downloadedUpdate;
    private bool _updatesReady, _updateBusy, _installingUpdate, _installedCopy;
    public event Action? UpdateInstallerStarted;

    private static ReleaseVersion CurrentVersion
    {
        get
        {
            string value = typeof(DashboardPage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
            return ReleaseVersion.TryParse(value, out var version) ? version : throw new InvalidOperationException("Version de Temhalo invalide.");
        }
    }

    private void InitializeUpdates()
    {
        InstalledVersionText.Text = "Temhalo " + CurrentVersion;
        try { _installedCopy = InstalledApplication.IsCurrentInstallation(AppContext.BaseDirectory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { _installedCopy = false; }
        CheckUpdatesAtStartup.IsOn = _settings.CheckUpdatesOnStartup;
        IncludePreviewUpdates.IsOn = _settings.IncludePreviewUpdates;
        _updatesReady = true;
        RefreshUpdates();
        if (_settings.CheckUpdatesOnStartup) _updateTask = RunUpdateOperationAsync(download: false, automatic: true);
    }

    private void RefreshUpdates()
    {
        if (!_updatesReady) return;
        CheckUpdatesButton.IsEnabled = !_updateBusy && !_installingUpdate;
        DownloadUpdateButton.Visibility = _availableUpdate is not null && _downloadedUpdate is null ? Visibility.Visible : Visibility.Collapsed;
        DownloadUpdateButton.IsEnabled = !_updateBusy;
        InstallUpdateButton.Visibility = _downloadedUpdate is not null && _installedCopy ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.IsEnabled = !_updateBusy && !_installingUpdate && !_session.Running && !_session.Stopping && !_relayStopping && !_starting && !_settingsBusy && !_accountMaintenance;
        CancelUpdateButton.Visibility = _updateBusy ? Visibility.Visible : Visibility.Collapsed;
        CheckUpdatesAtStartup.IsEnabled = IncludePreviewUpdates.IsEnabled = !_updateBusy && !_settingsBusy;
        UpdateInstallHint.Text = !_installedCopy
            ? "Copie portable ou de développement : lancez l’installeur depuis GitHub pour installer Temhalo. Votre dossier de sources ne sera pas modifié."
            : _session.Running || _session.Stopping || _starting
                ? "Vous pouvez télécharger pendant le live. Arrêtez le relais pour installer la mise à jour."
                : "L’installeur s’ouvrira et Temhalo se fermera. Vos réglages et vos comptes seront conservés. Cochez « Ouvrir Temhalo » à la fin pour le relancer.";
    }

    private async void CheckUpdatesClicked(object sender, RoutedEventArgs e)
    { if (!_updateBusy) await (_updateTask = RunUpdateOperationAsync(download: false)); }
    private async void DownloadUpdateClicked(object sender, RoutedEventArgs e)
    { if (!_updateBusy && _availableUpdate is not null) await (_updateTask = RunUpdateOperationAsync(download: true)); }
    private void CancelUpdateClicked(object sender, RoutedEventArgs e) => _updateCancellation?.Cancel();

    private async Task RunUpdateOperationAsync(bool download, bool automatic = false)
    {
        _updateBusy = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_updateLifetime.Token);
        _updateCancellation = cancellation;
        RefreshUpdates();
        try
        {
            var service = new GitHubUpdateService(_updateHttp);
            if (!download)
            {
                UpdateStatus.Text = "Recherche sur GitHub…";
                _availableUpdate = await service.CheckAsync(CurrentVersion, _settings.IncludePreviewUpdates, cancellation.Token);
                _downloadedUpdate = null;
                UpdateStatus.Text = _availableUpdate is { } release
                    ? $"La version {release.Version} est disponible."
                    : "Vous utilisez la dernière version disponible pour le canal choisi.";
                if (_availableUpdate is not null && automatic)
                    ShowInfo("Mise à jour disponible", $"Temhalo {_availableUpdate.Version} vous attend dans Réglages.");
            }
            else if (_availableUpdate is { } release)
            {
                UpdateProgress.Visibility = Visibility.Visible; UpdateProgress.Value = 0;
                UpdateStatus.Text = $"Téléchargement de Temhalo {release.Version}…";
                _downloadedUpdate = await service.DownloadAsync(release, AppStorage.DataDirectory,
                    new Progress<int>(percent => { if (!_closed) UpdateProgress.Value = percent; }), cancellation.Token);
                UpdateStatus.Text = _installedCopy ? "Téléchargement terminé. Empreinte SHA-256 vérifiée." :
                    "Téléchargement vérifié. Ouvrez le dossier pour lancer l’installeur.";
                if (!_installedCopy)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetDirectoryName(_downloadedUpdate.FilePath)!) { UseShellExecute = true });
            }
        }
        catch (OperationCanceledException)
        { UpdateStatus.Text = cancellation.IsCancellationRequested ? "Opération annulée." : "GitHub ne répond pas. Réessayez plus tard."; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or Win32Exception)
        {
            // Do not include arbitrary HTTP bodies/URLs in the UI or logs.
            UpdateStatus.Text = "Impossible de vérifier ou télécharger la mise à jour. Vérifiez votre connexion et réessayez. Aucun fichier n’a été installé.";
        }
        finally
        {
            _updateCancellation = null; _updateBusy = false;
            UpdateProgress.Visibility = Visibility.Collapsed;
            RefreshUpdates();
        }
    }

    private async void InstallUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (_downloadedUpdate is null || _updateBusy || _session.Running || _session.Stopping || _relayStopping || _starting || _settingsBusy || _accountMaintenance || _installingUpdate) return;
        _installingUpdate = true; Refresh();
        try
        {
            await InstalledApplication.LaunchInstallerAsync(_downloadedUpdate, AppContext.BaseDirectory, _updateLifetime.Token);
            UpdateInstallerStarted?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or OperationCanceledException)
        { UpdateStatus.Text = "Installation non démarrée. Relancez le téléchargement ou utilisez l’installeur depuis GitHub."; _installingUpdate = false; Refresh(); }
    }

    private async void UpdatePreferencesChanged(object sender, RoutedEventArgs e)
    {
        if (!_updatesReady || _settingsBusy || _updateBusy) return;
        _settingsBusy = true; Refresh();
        try
        {
            var updated = _settings with { CheckUpdatesOnStartup = CheckUpdatesAtStartup.IsOn, IncludePreviewUpdates = IncludePreviewUpdates.IsOn };
            await _settingsStore.SaveAsync(updated); _settings = updated;
            _availableUpdate = null; _downloadedUpdate = null;
            UpdateStatus.Text = "Préférences enregistrées. Recherchez les versions du canal choisi.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _updatesReady = false;
            CheckUpdatesAtStartup.IsOn = _settings.CheckUpdatesOnStartup; IncludePreviewUpdates.IsOn = _settings.IncludePreviewUpdates;
            _updatesReady = true;
            UpdateStatus.Text = "Impossible d’enregistrer les préférences de mise à jour.";
        }
        finally { _settingsBusy = false; Refresh(); }
    }
}
