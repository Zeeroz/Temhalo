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
    private bool _setupGuiding, _editorSaving;
    private string? _editingDestinationId;
    private readonly Dictionary<string, TextBlock> _directStatuses = new();
    private bool _accountMaintenance;
    private Task? _accountMaintenanceTask;
    private Task<IReadOnlyList<RelayOutput>>? _accountPreparationTask;
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
        _setupGuiding = !HasPreparedOutput() || !_settings.ObsInstructionsAcknowledged;
        _setupStep = HasPreparedOutput() && !_settings.ObsInstructionsAcknowledged ? 2 : 1;
        TwitchMethod.Text = Accounts.IsConnectionConfigured(StreamingProvider.Twitch) ? "Connecter votre compte" : "Utiliser une clé de diffusion";
        YouTubeMethod.Text = Accounts.IsConnectionConfigured(StreamingProvider.YouTube) ? "Connecter votre compte" : "Utiliser une clé de diffusion";
        KickMethod.Text = Accounts.IsConnectionConfigured(StreamingProvider.Kick) ? "Connecter votre compte" : "Utiliser une clé de diffusion";
        RenderDestinations();
    }
    private void RenderDestinations()
    {
        if (DestinationsList is null) return;
        DestinationsList.Children.Clear(); _destinationStatuses.Clear();
        DirectOutputs.Children.Clear(); _directStatuses.Clear();
        foreach (var destination in _destinations)
        {
            var name = new TextBlock { Text = destination.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            var method = new TextBlock { Text = destination.Account is { } account
                ? "Compte lié · " + account.DisplayName + (account.BroadcastTitle.Length > 0 ? " · " + account.BroadcastTitle : "") +
                    (account.AccessToken.Length == 0 && account.RefreshToken.Length == 0 ? " · À reconnecter" : "")
                : "Clé manuelle · " + (destination.StreamKey.Length == 0 ? "À renseigner" : destination.RememberKey ? "Mémorisée" : "Cette session"),
                Style = (Style)Application.Current.Resources["TemhaloCaption"] };
            var status = new TextBlock { Style = (Style)Application.Current.Resources["TemhaloCaption"] };
            _destinationStatuses[destination.Id] = status;
            var details = new StackPanel { Spacing = 4 }; details.Children.Add(name); details.Children.Add(method); details.Children.Add(status);
            var actions = new WrapPanel { Spacing = 8 };
            var enabled = new CheckBox { Content = "Diffuser", IsChecked = destination.Enabled };
            AutomationProperties.SetName(enabled, "Diffuser vers " + destination.Name);
            enabled.Click += async (_, _) => await SaveDestinationSetAsync(_destinations.Select(value => value.Id == destination.Id ? value with { Enabled = enabled.IsChecked == true } : value).ToArray());
            var edit = new Button { Content = "Modifier" }; AutomationProperties.SetName(edit, "Modifier " + name.Text);
            edit.Click += async (_, _) => await EditDestinationAsync(_destinations.FirstOrDefault(value => value.Id == destination.Id));
            var remove = new Button { Content = "Retirer" }; AutomationProperties.SetName(remove, "Retirer " + name.Text);
            remove.Click += async (_, _) => await SaveDestinationSetAsync(_destinations.Where(value => value.Id != destination.Id).ToArray());
            actions.Children.Add(enabled); actions.Children.Add(edit); actions.Children.Add(remove);
            var row = new SettingRow { EditorWidth = 330 }; row.Children.Add(details); row.Children.Add(actions);
            DestinationsList.Children.Add(new Border { Child = row, Padding = new Thickness(0, 16, 0, 16),
                BorderBrush = Brush("TemhaloRuleBrush"), BorderThickness = new Thickness(0, 0, 0, 1) });
            if (!destination.Enabled) continue;
            var liveStatus = new TextBlock { Style = (Style)Application.Current.Resources["TemhaloCaption"], TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            _directStatuses[destination.Id] = liveStatus;
            var liveRow = new Grid { Padding = new Thickness(0, 12, 0, 16), ColumnSpacing = 16,
                BorderBrush = Brush("TemhaloRuleBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
            liveRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            liveRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            liveRow.Children.Add(new TextBlock { Text = destination.Name });
            Grid.SetColumn(liveStatus, 1); liveRow.Children.Add(liveStatus); DirectOutputs.Children.Add(liveRow);
        }
        UpdateDestinationLabels(); RefreshSetup();
    }
    private async void ChoosePlatformClicked(object sender, RoutedEventArgs e)
        => await EditDestinationAsync(null, int.Parse((string)((FrameworkElement)sender).Tag));
    private async void OtherProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OtherProvider.SelectedItem is not ComboBoxItem item) return;
        int provider = int.Parse((string)item.Tag); OtherProvider.SelectedIndex = -1;
        await EditDestinationAsync(null, provider);
    }
    private Task EditDestinationAsync(DestinationSettings? existing, int provider = 0)
    {
        if (_dialogOpen || _settingsBusy || _session.Running || _starting || _accountMaintenance || existing is null && _destinations.Count >= 4) return Task.CompletedTask;
        try
        {
            _activeEditor = new PlatformEditor(Accounts, existing, provider);
            _editingDestinationId = existing?.Id; _dialogOpen = true;
            EditorTitle.Text = (existing is null ? "Ajouter " : "Modifier ") + PlatformEditor.ProviderName(existing?.Provider ?? provider);
            SavePlatformButton.Content = existing is null ? "Ajouter la plateforme" : "Enregistrer";
            SavePlatformButton.IsEnabled = _activeEditor.CanSave;
            _activeEditor.ReadinessChanged += ready => SavePlatformButton.IsEnabled = ready && !_editorSaving;
            PlatformEditorHost.Content = _activeEditor;
            EditorContainer.Visibility = Visibility.Visible;
            RefreshSetup(); EditorContainer.StartBringIntoView();
        }
        catch (Exception ex) when (PlatformEditor.IsConnectionError(ex))
        { CancelEditor(); ShowError(PlatformEditor.SafeError(ex)); }
        return Task.CompletedTask;
    }
    private void CancelEditor()
    {
        if (_editorSaving) return;
        _activeEditor?.Close(); _activeEditor = null; _editingDestinationId = null;
        PlatformEditorHost.Content = null; EditorContainer.Visibility = Visibility.Collapsed;
        _dialogOpen = false; RefreshSetup();
    }
    private void CancelPlatformClicked(object sender, RoutedEventArgs e) => CancelEditor();
    private async void SavePlatformClicked(object sender, RoutedEventArgs e)
    {
        if (_activeEditor is not { } editor || _editorSaving || !editor.CanSave) return;
        _editorSaving = true; editor.IsEnabled = false; SavePlatformButton.IsEnabled = false;
        try
        {
            var value = editor.ValidatedValue();
            var values = _editingDestinationId is null ? _destinations.Append(value).ToArray()
                : _destinations.Select(item => item.Id == _editingDestinationId ? value : item).ToArray();
            if (await SaveDestinationSetAsync(values))
            { _editorSaving = false; CancelEditor(); }
        }
        catch (ArgumentException ex) { editor.Error(ex.Message); }
        finally
        {
            _editorSaving = false;
            if (_activeEditor == editor) { editor.IsEnabled = true; SavePlatformButton.IsEnabled = editor.CanSave; }
            Refresh();
        }
    }
    private async Task<bool> SaveDestinationSetAsync(DestinationSettings[] values)
    {
        if (_settingsBusy || _starting || _session.Running) return false;
        _settingsBusy = true; Refresh();
        try
        {
            foreach (var value in values) value.Validate();
            var updated = _settings with { Destinations = Persistable(values), ServerUrl = "", StreamKey = "",
                OutputMode = 1 };
            await _settingsStore.SaveAsync(updated, _accountLifetime.Token); _settings = updated;
            _destinations.Clear(); _destinations.AddRange(values);
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
    private async Task<IReadOnlyList<RelayOutput>> ReadOutputsAsync(CancellationToken token)
    {
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
        if (_editorSaving || step > 1 && !HasPreparedOutput() || step == 3 && (ObsAcknowledged.IsChecked != true || _session.Running)) return;
        CancelEditor(); _setupStep = step;
        RefreshSetup(); MainScroll.ChangeView(null, 0, null, true);
    }
    private bool HasPreparedOutput()
    {
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
        foreach (var (dot, number, step) in new[] { (PlatformsStepDot, PlatformsStepNumber, 1), (ObsStepDot, ObsStepNumber, 2), (LaunchStepDot, LaunchStepNumber, 3) })
        {
            dot.Background = step == _setupStep ? Brush("TemhaloHaloBrush") : Brush("TemhaloCanvasBrush");
            number.Foreground = step == _setupStep ? Brush("TemhaloOnBrandBrush") : Brush("TemhaloMutedBrush");
            dot.BorderBrush = step <= _setupStep ? Brush("TemhaloHaloBrush") : Brush("TemhaloControlBrush");
        }
        SetupSteps.Visibility = _setupGuiding ? Visibility.Visible : Visibility.Collapsed;
        PlatformsPanel.Visibility = !_setupGuiding || _setupStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        ObsPanel.Visibility = _setupGuiding && _setupStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        LaunchPanel.Visibility = _setupGuiding && _setupStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        DiffusionTitle.Text = _setupGuiding ? "Préparer votre diffusion" : "Diffusion";
        DiffusionDescription.Text = _setupGuiding ? "Trois étapes. Vos choix seront conservés pour vos prochains lives." : "Vos plateformes et les réglages pour relier OBS.";
        PlatformsTitle.Text = _destinations.Count == 0 ? "Où souhaitez-vous diffuser ?" : "Vos plateformes";
        ReviewObsButton.Visibility = _setupGuiding ? Visibility.Collapsed : Visibility.Visible;
        ReviewObsButton.IsEnabled = !_starting && !_editorSaving;
        bool editing = _activeEditor is not null;
        ProviderChoices.Visibility = !editing && _destinations.Count < 4 ? Visibility.Visible : Visibility.Collapsed;
        ProviderChoices.IsEnabled = !_session.Running && !_starting;
        PlatformLimit.Visibility = !editing && _destinations.Count >= 4 ? Visibility.Visible : Visibility.Collapsed;
        SavedDestinations.IsEnabled = !editing;
        PlatformsActions.Visibility = _setupGuiding ? Visibility.Visible : Visibility.Collapsed;
        PlatformsNext.IsEnabled = ObsStep.IsEnabled = ready && !editing && !_settingsBusy;
        ObsNext.IsEnabled = ready && !_settingsBusy && !_starting;
        LaunchStep.IsEnabled = ready && ObsAcknowledged.IsChecked == true && !editing && !_session.Running && !_starting;
        ActivateTemhalo.IsEnabled = !_starting && !_settingsBusy && !_accountMaintenance && !_session.Stopping;
        ActivateTemhalo.Visibility = _session.Running ? Visibility.Collapsed : Visibility.Visible;
        ActivateTemhalo.Content = _starting ? "Préparation…" : "Démarrer le relais";
        OpenDirect.Visibility = _session.Running ? Visibility.Visible : Visibility.Collapsed;
        ObsNext.Content = _session.Running ? "Revenir au Direct →" : "J’ai terminé dans OBS →";
        LaunchHelp.Text = "Démarrez le relais, puis lancez votre stream dans OBS.";
        StartStopButton.Visibility = _setupGuiding && DestinationView.Visibility == Visibility.Visible && !_session.Running && !_starting
            ? Visibility.Collapsed : Visibility.Visible;
        StartStopButton.Style = _session.Running ? null : (Style)Application.Current.Resources["TemhaloPrimaryButton"];
        DestinationHelp.Text = _session.Running ? "Arrêtez le relais pour modifier les plateformes." : editing ? "Terminez ou annulez la modification pour continuer."
            : !ready ? "Ajoutez une plateforme ou reconnectez les comptes signalés avant de continuer."
            : _setupGuiding ? "Votre plateforme est configurée. Continuez vers OBS." : "Toutes les destinations activées reçoivent le même délai.";
    }
    private void SetupStepClicked(object sender, RoutedEventArgs e) => ShowSetupStep(int.Parse((string)((FrameworkElement)sender).Tag));
    private void PlatformsNextClicked(object sender, RoutedEventArgs e) => ShowSetupStep(2);
    private async void ObsNextClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsBusy || _starting) return;
        if (_session.Running) { _setupGuiding = false; Navigate("direct"); return; }
        if (ObsAcknowledged.IsChecked == true) { ShowSetupStep(3); return; }
        _settingsBusy = true; RefreshSetup();
        try
        {
            var updated = _settings with { ObsInstructionsAcknowledged = true };
            await _settingsStore.SaveAsync(updated, _accountLifetime.Token); _settings = updated;
            ObsAcknowledged.IsChecked = true; ShowSetupStep(3);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError(ex.Message); }
        finally { _settingsBusy = false; Refresh(); }
    }
    private void OpenDirectClicked(object sender, RoutedEventArgs e) { _setupGuiding = false; Navigate("direct"); }
    private void CopyObsValueClicked(object sender, RoutedEventArgs e)
    {
        try { var data = new DataPackage(); data.SetText((string)((FrameworkElement)sender).Tag); Clipboard.SetContent(data); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        { ShowError("Le presse-papiers est indisponible. Sélectionnez et copiez la valeur affichée."); }
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
            label.Foreground = Brush(_session.Running && output?.Ready == true ? "TemhaloSuccessBrush" : output?.Reconnecting == true ? "TemhaloWarningBrush" : "TemhaloMutedBrush");
            if (_directStatuses.TryGetValue(destination.Id, out var direct))
            {
                direct.Text = !_session.Running ? "Prête au démarrage" : label.Text;
                direct.Foreground = label.Foreground;
            }
        }
        ActiveOutputs.Text = outputs.Count > 1 && _session.Running ? string.Join("\n", outputs.Select(output =>
            output.Name + " · " + (output.Ready ? "Émission active" : output.Reconnecting ? "Reconnexion" : "En attente") +
            (output.ExtraDelayMilliseconds >= 1000 ? $" · retard supplémentaire +{output.ExtraDelayMilliseconds / 1000}s" : ""))) : "";
        ActiveOutputs.Visibility = ActiveOutputs.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
