using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Temhalo.App.Controls;
using Temhalo.Infrastructure.Windows.Accounts;
using Temhalo.Infrastructure.Windows.Logging;
using Temhalo.Infrastructure.Windows.Processes;
using Temhalo.Infrastructure.Windows.Settings;
using Windows.ApplicationModel.DataTransfer;

namespace Temhalo.App.Views;

public sealed partial class DashboardPage
{
    private readonly List<DestinationSettings> _destinations = [];
    private readonly Dictionary<string, TextBlock> _destinationStatuses = new();
    private readonly HttpClient _accountHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    private readonly CancellationTokenSource _accountLifetime = new();
    private StreamingAccountService? _accounts;
    private PlatformEditor? _activeEditor;
    private int _setupStep = 1;
    private bool _accountMaintenance;
    private Task? _accountMaintenanceTask;
    private Task<IReadOnlyList<RelayOutput>?>? _accountPreparationTask;
    private DateTimeOffset _nextAccountValidation = DateTimeOffset.MinValue;

    private StreamingAccountService Accounts
    {
        get
        {
            if (_accounts is not null) return _accounts;
            OAuthClientConfiguration configuration;
            try { configuration = OAuthClientConfiguration.Load(Path.GetDirectoryName(_settingsStore.FilePath)!); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { configuration = new(); ShowInfo("Configuration des comptes à vérifier", "Vérifiez oauth-clients.json. La saisie manuelle reste disponible."); }
            return _accounts = new(_accountHttp, configuration, (uri, token) =>
            { token.ThrowIfCancellationRequested(); Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); return Task.CompletedTask; });
        }
    }

    private void InitializeDestinations()
    {
        _destinations.Clear();
        if (_settings.Destinations is { } saved) _destinations.AddRange(saved);
        else if (_settings.ServerUrl.Length > 0)
            _destinations.Add(new() { Name = "Sortie principale", Provider = _settings.Provider,
                ServerUrl = _settings.ServerUrl, StreamKey = _settings.StreamKey });
        foreach (var destination in _destinations)
        { SecretRedactor.Shared.Remember(destination.StreamKey); destination.Account?.ProtectDiagnostics(); }
        ObsAcknowledged.IsChecked = _settings.ObsInstructionsAcknowledged;
        RenderDestinations(); ShowSetupStep(1);
    }
    private void RenderDestinations()
    {
        if (DestinationsList is null) return;
        DestinationsList.Children.Clear(); _destinationStatuses.Clear();
        foreach (var destination in _destinations)
        {
            var name = new TextBlock { Text = destination.Name, Style = (Style)Application.Current.Resources["TemhaloSectionTitle"] };
            var method = new TextBlock { Text = destination.Account is { } account
                ? "Compte lié · " + account.DisplayName + (account.BroadcastTitle.Length > 0 ? " · " + account.BroadcastTitle : "") +
                    (account.AccessToken.Length == 0 && account.RefreshToken.Length == 0 ? " · À reconnecter" : "")
                : "Clé manuelle · " + (destination.StreamKey.Length == 0 ? "À renseigner" : destination.RememberKey ? "Mémorisée" : "Cette session"),
                Style = (Style)Application.Current.Resources["TemhaloCaption"] };
            var status = new TextBlock { FontSize = 12, Foreground = Brush("TemhaloMutedBrush"), TextWrapping = TextWrapping.Wrap };
            _destinationStatuses[destination.Id] = status;
            var details = new StackPanel { Spacing = 4 }; details.Children.Add(name); details.Children.Add(method); details.Children.Add(status);
            var actions = new WrapPanel { Spacing = 8 };
            var enabled = new CheckBox { Content = "Activer", IsChecked = destination.Enabled };
            enabled.Click += async (_, _) => await SaveDestinationSetAsync(_destinations.Select(value => value.Id == destination.Id ? value with { Enabled = enabled.IsChecked == true } : value).ToArray());
            var edit = new Button { Content = "Modifier" }; AutomationProperties.SetName(edit, "Modifier " + name.Text);
            edit.Click += async (_, _) => await EditDestinationAsync(_destinations.FirstOrDefault(value => value.Id == destination.Id));
            var remove = new Button { Content = "Retirer" }; AutomationProperties.SetName(remove, "Retirer " + name.Text);
            remove.Click += async (_, _) => await SaveDestinationSetAsync(_destinations.Where(value => value.Id != destination.Id).ToArray());
            actions.Children.Add(enabled); actions.Children.Add(edit); actions.Children.Add(remove);
            var row = new SettingRow(); row.Children.Add(details); row.Children.Add(actions);
            DestinationsList.Children.Add(new Border { Child = row, Padding = new Thickness(16), CornerRadius = new CornerRadius(8), Background = Brush("TemhaloCanvasBrush") });
        }
        EmptyPlatforms.Visibility = _destinations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddDestinationButton.Visibility = _destinations.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        AddDestinationButton.IsEnabled = _destinations.Count < 4;
        UpdateDestinationLabels(); RefreshSetup();
    }
    private async void AddDestinationClicked(object sender, RoutedEventArgs e) => await EditDestinationAsync(null);
    private async void ChoosePlatformClicked(object sender, RoutedEventArgs e)
        => await EditDestinationAsync(null, int.Parse((string)((FrameworkElement)sender).Tag));
    private async Task EditDestinationAsync(DestinationSettings? existing, int provider = 0)
    {
        if (_dialogOpen || _settingsBusy || _session.Running || _starting || _accountMaintenance || existing is null && _destinations.Count >= 4) return;
        _dialogOpen = true;
        PlatformEditor? editor = null;
        try
        {
            editor = new PlatformEditor(Accounts, existing, provider); _activeEditor = editor;
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = existing is null ? "Ajouter une plateforme" : "Modifier la plateforme",
                Content = editor, PrimaryButtonText = existing is null ? "Ajouter la plateforme" : "Enregistrer", CloseButtonText = "Annuler", DefaultButton = ContentDialogButton.Primary,
                RequestedTheme = ElementTheme.Dark };
            dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["TemhaloPrimaryButton"];
            dialog.IsPrimaryButtonEnabled = editor.CanSave;
            dialog.Resources["ContentDialogBackground"] = Brush("TemhaloSurfaceBrush");
            dialog.Resources["ContentDialogTopOverlay"] = Brush("TemhaloSurfaceBrush");
            dialog.Resources["ContentDialogBorderBrush"] = Brush("TemhaloRuleBrush");
            editor.ReadinessChanged += ready => dialog.IsPrimaryButtonEnabled = ready;
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    var value = editor.ValidatedValue();
                    var values = existing is null ? _destinations.Append(value).ToArray() : _destinations.Select(item => item.Id == existing.Id ? value : item).ToArray();
                    if (!await SaveDestinationSetAsync(values, switchToNetwork: true)) args.Cancel = true;
                }
                catch (ArgumentException ex) { args.Cancel = true; editor.Error(ex.Message); }
                finally { deferral.Complete(); }
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex) when (PlatformEditor.IsConnectionError(ex)) { ShowError(ex is IOException ? ex.Message : PlatformEditor.SafeError(ex)); }
        finally { editor?.Close(); _activeEditor = null; _dialogOpen = false; Refresh(); }
    }
    private async Task<bool> SaveDestinationSetAsync(DestinationSettings[] values, bool switchToNetwork = false)
    {
        if (_settingsBusy || _starting || _session.Running) return false;
        _settingsBusy = true; Refresh();
        try
        {
            foreach (var value in values) value.Validate();
            var updated = _settings with { Destinations = Persistable(values), ServerUrl = "", StreamKey = "",
                OutputMode = switchToNetwork ? 1 : OutputMode.SelectedIndex };
            await _settingsStore.SaveAsync(updated, _accountLifetime.Token); _settings = updated;
            _destinations.Clear(); _destinations.AddRange(values); OutputMode.SelectedIndex = updated.OutputMode;
            RenderDestinations(); return true;
        }
        catch (OperationCanceledException) when (_closed || _accountLifetime.IsCancellationRequested) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { ShowError(SecretRedactor.Shared.Redact(ex.Message)); RenderDestinations(); return false; }
        finally { _settingsBusy = false; Refresh(); }
    }
    private static DestinationSettings[] Persistable(IEnumerable<DestinationSettings> values) => values.Select(destination =>
        destination.Account is { } account ? destination with { ServerUrl = "", StreamKey = "", Account = destination.RememberKey ? account : account with { AccessToken = "", RefreshToken = "" } }
        : destination.RememberKey ? destination : destination with { StreamKey = "" }).ToArray();

    private async Task CommitRefreshedAccountAsync(string id, ConnectedAccount account, CancellationToken token)
    {
        // Once the server rotated a token, cancellation must not discard its replacement.
        using var durability = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (_settingsBusy) await Task.Delay(50, durability.Token);
        int index = _destinations.FindIndex(value => value.Id == id);
        if (index < 0) return;
        _settingsBusy = true;
        try
        {
            _destinations[index] = _destinations[index] with { Account = account };
            var updated = _settings with { Destinations = Persistable(_destinations), ServerUrl = "", StreamKey = "" };
            // Save before the next API request: Twitch refresh tokens rotate, even if that request fails.
            await _settingsStore.SaveAsync(updated, durability.Token); _settings = updated;
        }
        finally { _settingsBusy = false; }
    }
    private async Task<IReadOnlyList<RelayOutput>?> ReadOutputsAsync(CancellationToken token)
    {
        if (OutputMode.SelectedIndex == 0) return null;
        var enabled = _destinations.Where(destination => destination.Enabled).ToArray();
        if (enabled.Length == 0) throw new ArgumentException("Ajoutez puis activez au moins une destination.");
        var outputs = new List<RelayOutput>();
        foreach (var destination in enabled)
        {
            destination.Validate(requireKey: true);
            if (destination.Account is not { } account)
            { outputs.Add(new(destination.Id, destination.Name, new(destination.ServerUrl, destination.StreamKey))); continue; }
            var fresh = await Accounts.RefreshAsync(account, token);
            if (fresh != account) await CommitRefreshedAccountAsync(destination.Id, fresh, token);
            fresh = await Accounts.IdentifyAsync(fresh, token);
            await CommitRefreshedAccountAsync(destination.Id, fresh, token);
            var stream = await Accounts.ResolveStreamAsync(fresh, token);
            outputs.Add(new(destination.Id, destination.Name, new(stream.ServerUrl, stream.StreamKey)));
        }
        _nextAccountValidation = DateTimeOffset.UtcNow.AddMinutes(55);
        token.ThrowIfCancellationRequested();
        return outputs;
    }
    private void MaintainTwitchAccounts()
    {
        if (_closed || _settingsBusy || _starting || _dialogOpen || _accountMaintenance || DateTimeOffset.UtcNow < _nextAccountValidation ||
            !_destinations.Any(value => value.Account is { Provider: StreamingProvider.Twitch, AccessToken.Length: > 0 })) return;
        _accountMaintenance = true; _nextAccountValidation = DateTimeOffset.UtcNow.AddMinutes(5);
        _accountMaintenanceTask = ValidateTwitchAccountsAsync();
    }
    private async Task ValidateTwitchAccountsAsync()
    {
        try
        {
            foreach (var destination in _destinations.Where(value => value.Account?.Provider == StreamingProvider.Twitch).ToArray())
            {
                var current = destination.Account!;
                var fresh = await Accounts.RefreshAsync(current, _accountLifetime.Token);
                if (fresh != current) await CommitRefreshedAccountAsync(destination.Id, fresh, _accountLifetime.Token);
                fresh = await Accounts.IdentifyAsync(fresh, _accountLifetime.Token);
                await CommitRefreshedAccountAsync(destination.Id, fresh, _accountLifetime.Token);
            }
            _nextAccountValidation = DateTimeOffset.UtcNow.AddMinutes(55);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (PlatformEditor.IsConnectionError(ex))
        { if (!_closed) DestinationHelp.Text = PlatformEditor.SafeError(ex); }
        finally { _accountMaintenance = false; }
    }
    private void ShowSetupStep(int step)
    {
        if (step > 1 && !HasPreparedOutput() || step == 3 && ObsAcknowledged.IsChecked != true) return;
        _setupStep = step;
        PlatformsPanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        ObsPanel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        LaunchPanel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        RefreshSetup(); MainScroll.ChangeView(null, 0, null, true);
    }
    private bool HasPreparedOutput()
    {
        if (OutputMode.SelectedIndex == 0) return true;
        var enabled = _destinations.Where(value => value.Enabled).ToArray();
        return enabled.Length > 0 && enabled.All(value => value.Account is { } account
            ? (account.AccessToken.Length > 0 || account.RefreshToken.Length > 0) && (account.Provider != StreamingProvider.YouTube || account.BroadcastId.Length > 0)
            : value.StreamKey.Length > 0);
    }
    private void RefreshSetup()
    {
        if (PlatformsStep is null || ObsAcknowledged is null) return;
        bool ready = HasPreparedOutput();
        PlatformsStep.IsChecked = _setupStep == 1; ObsStep.IsChecked = _setupStep == 2; LaunchStep.IsChecked = _setupStep == 3;
        PlatformsNext.IsEnabled = ObsStep.IsEnabled = ready;
        ObsNext.IsEnabled = LaunchStep.IsEnabled = ready && ObsAcknowledged.IsChecked == true;
        ActivateTemhalo.IsEnabled = !_starting && !_settingsBusy && !_accountMaintenance && !_session.Stopping;
        ActivateTemhalo.Visibility = _session.Running ? Visibility.Collapsed : Visibility.Visible;
        ActivateTemhalo.Content = _starting ? "Préparation des destinations…" : "Activer Temhalo";
        OpenDirect.Visibility = _session.Running ? Visibility.Visible : Visibility.Collapsed;
        LaunchHelp.Text = _session.Running ? "Temhalo est actif. Lancez le stream dans OBS ; les connexions ci-dessous se mettront à jour." : "Activez Temhalo, puis cliquez sur Commencer le streaming dans OBS.";
        bool diffusion = DestinationView.Visibility == Visibility.Visible;
        StartStopButton.Visibility = diffusion && !_session.Running ? Visibility.Collapsed : Visibility.Visible;
        DestinationHelp.Text = OutputMode.SelectedIndex == 0 ? "Test local sélectionné : aucune plateforme ne sera contactée." :
            ready ? "Les connexions sont enregistrées. Continuez vers OBS." : "Ajoutez une plateforme ou reconnectez les comptes signalés avant de continuer.";
    }
    private void SetupStepClicked(object sender, RoutedEventArgs e) => ShowSetupStep(int.Parse((string)((FrameworkElement)sender).Tag));
    private void PlatformsNextClicked(object sender, RoutedEventArgs e) => ShowSetupStep(2);
    private void ObsNextClicked(object sender, RoutedEventArgs e) => ShowSetupStep(3);
    private void OpenDirectClicked(object sender, RoutedEventArgs e) => Navigate("direct");
    private void CopyObsValueClicked(object sender, RoutedEventArgs e)
    { var data = new DataPackage(); data.SetText((string)((FrameworkElement)sender).Tag); Clipboard.SetContent(data); }
    private async void ObsAcknowledgedChanged(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoaded || _settingsBusy || _starting || _closed) return;
        _settingsBusy = true;
        try { var updated = _settings with { ObsInstructionsAcknowledged = ObsAcknowledged.IsChecked == true }; await _settingsStore.SaveAsync(updated, _accountLifetime.Token); _settings = updated; }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError(ex.Message); ObsAcknowledged.IsChecked = _settings.ObsInstructionsAcknowledged; }
        finally { _settingsBusy = false; RefreshSetup(); }
    }
    private void RefreshDestinationStates(LocalRelaySnapshot? snapshot)
    {
        var outputs = snapshot?.Outputs ?? [];
        if (_session.Running && outputs.Count > 1) Vm.OutputText = $"{outputs.Count(output => output.Ready)}/{outputs.Count} sorties actives";
        foreach (var destination in _destinations)
        {
            if (!_destinationStatuses.TryGetValue(destination.Id, out var label)) continue;
            var output = outputs.FirstOrDefault(value => value.Id == destination.Id);
            label.Text = !destination.Enabled ? "Désactivée" : !_session.Running ? "Émission arrêtée" : output?.Ready == true ? "Émission active" :
                output?.Reconnecting == true ? "Reconnexion automatique" : "En attente du média";
            if (_session.Running && output is { ExtraDelayMilliseconds: >= 1000 }) label.Text += $" · retard supplémentaire {output.ExtraDelayMilliseconds / 1000}s";
            label.Foreground = Brush(_session.Running && output?.Ready == true ? "TemhaloSuccessBrush" : "TemhaloMutedBrush");
        }
        ActiveOutputs.Text = outputs.Count > 1 && _session.Running ? string.Join("\n", outputs.Select(output =>
            output.Name + " · " + (output.Ready ? "Émission active" : output.Reconnecting ? "Reconnexion" : "En attente") +
            (output.ExtraDelayMilliseconds >= 1000 ? $" · retard supplémentaire +{output.ExtraDelayMilliseconds / 1000}s" : ""))) : "";
        ActiveOutputs.Visibility = ActiveOutputs.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
