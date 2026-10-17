using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Temhalo.App.Services;
using Temhalo.Infrastructure.Windows.Logging;
using Temhalo.Infrastructure.Windows.Processes;
using Temhalo.Presentation;
using Temhalo.Infrastructure.Windows.Hotkeys;
using Temhalo.Infrastructure.Windows.Settings;

namespace Temhalo.App.Views;

public sealed partial class DashboardPage : Page
{
    private readonly RelaySession _session = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ObservableCollection<string> _journal = new();
    private bool _starting, _closed, _relayStopping;
    private Task _relayOperationTask = Task.CompletedTask;
    private string? _shownError;
    private string? _layout;
    private readonly DpapiSettingsStore _settingsStore = CreateSettingsStore();
    private AppSettings _settings = new();
    private GlobalHotkeyService? _hotkeys;
    private bool _settingsBusy = true, _settingsLoaded;
    public DashboardViewModel Vm { get; }

    // An explicit directory isolates native UI validation from the user's encrypted configuration.
    private static DpapiSettingsStore CreateSettingsStore()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--settings-directory");
        return index >= 0 && index + 1 < args.Length
            ? new DpapiSettingsStore(Path.GetFullPath(args[index + 1])) : new DpapiSettingsStore(AppStorage.DataDirectory);
    }

    public DashboardPage()
    {
        Vm = new DashboardViewModel(seconds => _session.RequestDelay(seconds));
        InitializeComponent();
        Navigation.SelectedItem = Navigation.MenuItems[0];
        SettingsNavigation.SelectedItem = SettingsNavigation.Items[1];
        Vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(Vm.TargetSeconds)) UpdatePresetSelection(); };
        RenderPresets();
        JournalList.ItemsSource = _journal;
        _refresh.Tick += (_, _) => Refresh();
        Loaded += async (_, _) =>
        {
            UpdateLayoutState(); _refresh.Start(); Refresh();
            if (!_settingsLoaded) { _settingsLoaded = true; await InitializeSettingsAsync(); InitializeUpdates(); }
        };
    }

    public void AttachWindow(nint handle)
    {
        _windowHandle = handle;
        try
        {
            _hotkeys = new GlobalHotkeyService(handle);
            _hotkeys.Invoked += HotkeyInvoked;
        }
        catch (InvalidOperationException ex) { ShowError(ex.Message); }
    }

    private async Task InitializeSettingsAsync()
    {
        try
        {
            _settings = await _settingsStore.LoadAsync();
            SecretRedactor.Shared.Remember(_settings.StreamKey);
            if (!File.Exists(_settingsStore.FilePath)) _settings = _settings with { OutputMode = 1 };
            OutputMode.SelectedIndex = _settings.OutputMode;
            MemoryLimit.Value = _settings.MemoryLimitMiB;
            InitializeDestinations();
            _preferences = _settings.Interface ?? new();
            RenderPresets(); UpdateDestinationLabels();
            var keys = _settings.Hotkeys;
            HotkeysEnabled.IsOn = keys.Enabled; ProtectionSeconds.Value = keys.ProtectionSeconds;
            ProtectionHotkey.Text = keys.Protection; ZeroHotkey.Text = keys.Zero;
            IncreaseHotkey.Text = keys.Increase; DecreaseHotkey.Text = keys.Decrease;
            try
            {
                _hotkeys?.Apply(keys);
                HotkeyStatus.Text = keys.Enabled && _hotkeys is not null ? "Raccourcis actifs dans Windows." : "Raccourcis désactivés.";
            }
            catch (InvalidOperationException ex)
            {
                // A competing shortcut must not prevent the independent OBS web service from starting.
                HotkeyStatus.Text = "Raccourcis indisponibles. Choisissez d'autres combinaisons puis enregistrez.";
                ShowError(SecretRedactor.Shared.Redact(ex.Message));
            }
            _session.RecordControlEvent("Configuration chargée ; aucun démarrage automatique de diffusion.");
            await InitializeLocalServerAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { HotkeyStatus.Text = "Raccourcis inactifs. Vérifiez puis appliquez les réglages."; ShowError(SecretRedactor.Shared.Redact(ex.Message)); }
        finally { _settingsBusy = false; await InitializeStreamDeckAsync(); Refresh(); }
    }

    private void HotkeyInvoked(HotkeyAction action)
    {
        if (_closed || _settingsBusy || _starting || _session.Stopping || !_session.Running) return;
        switch (action)
        {
            case HotkeyAction.Protection: Vm.RequestTarget(_settings.Hotkeys.ProtectionSeconds); break;
            case HotkeyAction.Zero: Vm.RequestTarget(0); break;
            case HotkeyAction.Increase: Vm.AdjustTarget(10); break;
            case HotkeyAction.Decrease: Vm.AdjustTarget(-10); break;
        }
        _session.RecordControlEvent("Raccourci global exécuté : " + GlobalHotkeyService.Label(action));
        Refresh();
    }

    private static int ReadInteger(double value, int minimum, int maximum, string error)
        => double.IsFinite(value) && value >= minimum && value <= maximum && value == Math.Truncate(value)
            ? (int)value : throw new ArgumentException(error);

    private async void SaveDestinationClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsBusy || _session.Running || _starting) return;
        await SaveDestinationSetAsync(_destinations.ToArray());
    }

    private async void SaveHotkeysClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsBusy || _hotkeys is null) return;
        _settingsBusy = true; Refresh();
        var previous = _settings.Hotkeys;
        bool applied = false;
        try
        {
            var keys = new HotkeySettings { Enabled = HotkeysEnabled.IsOn,
                ProtectionSeconds = ReadInteger(ProtectionSeconds.Value, 0, 300, "Le preset doit être un entier de 0 à 300 s."),
                Protection = ProtectionHotkey.Text, Zero = ZeroHotkey.Text,
                Increase = IncreaseHotkey.Text, Decrease = DecreaseHotkey.Text };
            _hotkeys.Apply(keys); applied = true;
            var updated = _settings with { Hotkeys = keys };
            await _settingsStore.SaveAsync(updated); _settings = updated;
            HotkeyStatus.Text = keys.Enabled ? "Raccourcis actifs et enregistrés." : "Raccourcis désactivés et réglages enregistrés.";
            _session.RecordControlEvent("Configuration des raccourcis mise à jour.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            if (applied) try { _hotkeys.Apply(previous); }
                catch (InvalidOperationException) { HotkeyStatus.Text = "Les raccourcis sont indisponibles. Appliquez de nouvelles combinaisons."; }
            ShowError(SecretRedactor.Shared.Redact(ex.Message));
        }
        finally { _settingsBusy = false; Refresh(); }
    }

    private void LayoutChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutState();
    private void UpdateLayoutState()
    {
        if (SessionLabel is null) return;
        SessionLabel.Visibility = ActualWidth < 400 ? Visibility.Collapsed : Visibility.Visible;
        bool narrow = ActualWidth < 760;
        Grid.SetRow(SessionCommands, narrow ? 1 : 0);
        Grid.SetColumn(SessionCommands, narrow ? 0 : 1);
        Grid.SetColumnSpan(SessionCommands, narrow ? 2 : 1);
        SessionCommands.Margin = narrow ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        bool tiny = ActualWidth < 540;
        Grid.SetColumnSpan(CurrentGroup, tiny ? 2 : 1);
        Grid.SetColumnSpan(TargetGroup, tiny ? 2 : 1);
        Grid.SetColumn(TargetGroup, tiny ? 0 : 1);
        Grid.SetRow(TargetGroup, tiny ? 1 : 0);
        DelayValues.RowSpacing = tiny ? 12 : 0;
        SettingsNavigation.Visibility = ActualWidth < 700 ? Visibility.Collapsed : Visibility.Visible;
        CompactSettingsNavigation.Visibility = ActualWidth < 700 ? Visibility.Visible : Visibility.Collapsed;
        string state = ActualWidth - 56 >= 920 ? "WideLayout" : "CompactLayout";
        if (_layout == state) return;
        _layout = state;
        VisualStateManager.GoToState(this, state, false);
    }

    private void NavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (DashboardView is null) return;
        string tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "direct";
        DashboardView.Visibility = tag == "direct" ? Visibility.Visible : Visibility.Collapsed;
        DestinationView.Visibility = tag == "diffusion" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        RefreshSetup();
        MainScroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void CustomClicked(object sender, RoutedEventArgs e)
    {
        ((Microsoft.UI.Xaml.Controls.Primitives.ToggleButton)sender).IsChecked = Vm.PresetCustom;
        DelayNumber.Focus(FocusState.Programmatic);
    }
    private void OutputModeChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDestinationLabels();
    }

    private async void StartStopClicked(object sender, RoutedEventArgs e)
    {
        var state = ReadRelayControlState();
        if (!(state.CanStart || state.CanStop)) return;
        await (_relayOperationTask = ChangeRelayRunningAsync(!_session.Running));
    }

    private async Task ChangeRelayRunningAsync(bool start)
    {
        if (!start)
        {
            _relayStopping = true;
            try { var stopped = _session.StopAsync(); Refresh(); await stopped; }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            { ShowError(SecretRedactor.Shared.Redact(ex.Message)); }
            finally { _relayStopping = false; Refresh(); }
            return;
        }
        try
        {
            _starting = true; Vm.CanEdit = false; StartStopButton.IsEnabled = false; Notice.IsOpen = false;
            Refresh();
            if (!double.IsFinite(MemoryLimit.Value) || MemoryLimit.Value is < 1 or > 4096 || Math.Truncate(MemoryLimit.Value) != MemoryLimit.Value)
                throw new ArgumentException("La limite mémoire doit être un entier de 1 à 4096 Mio.");
            using var preparation = CancellationTokenSource.CreateLinkedTokenSource(_accountLifetime.Token);
            preparation.CancelAfter(TimeSpan.FromMinutes(2));
            _accountPreparationTask = ReadOutputsAsync(preparation.Token);
            var outputs = await _accountPreparationTask;
            await _session.StartAsync(Vm.TargetSeconds, checked((long)MemoryLimit.Value * 1048576), null, outputs, PreviewOutput()?.Target, _accountLifetime.Token);
            OutputTitle.Text = outputs is null ? "Sortie fichier local" : $"Sorties · {outputs.Count} destination(s)";
            OutputDetail.Text = outputs is null ? "FLV · H.264 / AAC · passthrough" : "RTMP / RTMPS · passthrough";
            _shownError = null;
        }
        catch (Temhalo.Infrastructure.Windows.Accounts.AccountConnectionException ex) { ShowError(ex.Message); }
        catch (Exception ex) when (ex is KeyNotFoundException or FormatException) { ShowError("La plateforme a renvoyé une réponse inattendue. Reconnectez le compte ou utilisez une clé manuelle."); }
        catch (OperationCanceledException) when (_localShuttingDown || _closed) { }
        catch (OperationCanceledException) { ShowError("La préparation des outils a été interrompue ou a dépassé son délai. Réessayez Démarrer."); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or UnauthorizedAccessException or HttpRequestException)
        { ShowError(SecretRedactor.Shared.Redact(ex.Message)); }
        finally { _starting = false; RenderDestinations(); Refresh(); }
    }

    private void Refresh()
    {
        if (_closed) return;
        _session.ObserveLive();
        RefreshUpdates();
        var snapshot = _session.Snapshot;
        Vm.ConfiguredMemoryLimitMiB = _settings.MemoryLimitMiB;
        Vm.Refresh(snapshot?.Delay, _session.Running, _session.Stopping, snapshot?.IngestConnected == true,
            snapshot?.OutputReady == true, _session.Network, snapshot?.State ?? "Stopped", snapshot?.Attempt ?? 0,
            snapshot?.BytesTransferred ?? 0, _session.ElapsedSeconds, _session.LiveDuration);
        RefreshDestinationStates(snapshot);
        RefreshPreview();
        if (_starting) Vm.CanEdit = false;
        DestinationFields.IsEnabled = !_session.Running && !_starting && !_settingsBusy && !_accountMaintenance;
        HotkeyFields.IsEnabled = !_settingsBusy && !_session.Stopping;
        var relayControl = ReadRelayControlState();
        StartStopButton.IsEnabled = relayControl.CanStart || relayControl.CanStop;
        OutputMode.IsEnabled = !_session.Running && !_starting && !_settingsBusy && !_dialogOpen && !_accountMaintenance;
        ObsAcknowledged.IsEnabled = !_settingsBusy && !_starting;
        RefreshSetup(); MaintainTwitchAccounts();
        StartStopButton.Content = _starting ? "Démarrage…" : _relayStopping || _session.Stopping ? "Vidange en cours…" : _session.Running ? "Arrêter le relais" : "Démarrer le relais";
        LastEvent.Text = snapshot?.State == "RetryingOutput" ? "Reconnexion automatique de la sortie. Le buffer RAM est conservé." :
            _session.LastAction ?? snapshot?.LastEvent ?? "Prêt. Démarrez le relais, puis lancez la publication OBS.";
        SessionPath.Text = _session.DirectoryPath ?? "Aucune session démarrée.";
        IngestState.Foreground = Brush(snapshot?.IngestConnected == true && _session.Running ? "TemhaloSuccessBrush" : "TemhaloMutedBrush");
        OutputState.Foreground = Brush(snapshot?.OutputReady == true && _session.Running ? "TemhaloSuccessBrush" : "TemhaloMutedBrush");
        BufferBar.Foreground = Brush(Vm.BufferWarning ? "TemhaloWarningBrush" : "TemhaloControlBrush");
        FooterInput.Text = "OBS · " + Vm.IngestText; FooterInput.Foreground = IngestState.Foreground;
        FooterOutput.Text = "Sortie · " + Vm.OutputText; FooterOutput.Foreground = OutputState.Foreground;
        TimeRail.Update(_session.Running ? snapshot?.Delay?.LastMediaAgeMilliseconds : null, Vm.TargetSeconds);
        RateChart.Update(Vm.Rates.Points);
        ReserveValue.Text = $"{snapshot?.Delay?.BufferedTags ?? 0:N0} tags FLV";
        ReconnectValue.Text = (snapshot?.Outputs is { Count: > 1 } outputs ? outputs.Sum(output => Math.Max(0, output.Attempt - 1)) : snapshot?.Delay?.OutputReconnections ?? 0).ToString();
        SkippedValue.Text = $"{(snapshot?.Delay?.SkippedMediaTags ?? 0) + (snapshot?.Outputs?.Sum(output => output.SkippedTags) ?? 0):N0} tags FLV";
        NewPreset.IsEnabled = !_settingsBusy;
        CopyDockButton.IsEnabled = _localServer?.Address is not null;
        PublishLocalState(); RefreshStreamDeckStatus();
        while (_session.TryReadEvent(out string? value))
        {
            _journal.Add(value!);
            while (_journal.Count > 200) _journal.RemoveAt(0);
        }
        if (_session.Error is { } error && error != _shownError) { _shownError = error; ShowError(error); }
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    private void NoticeClosed(InfoBar sender, InfoBarClosedEventArgs args) => Notice.Visibility = Visibility.Collapsed;
    private void ShowError(string error)
    { Notice.Severity = InfoBarSeverity.Error; Notice.Title = "Le relais demande votre attention"; Notice.Message = SecretRedactor.Shared.Redact(error); Notice.Visibility = Visibility.Visible; Notice.IsOpen = true; MainScroll.ChangeView(null, 0, null, true); _session.RecordControlEvent(error); }

    private void OpenLogsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_session.LogsDirectoryPath);
            Process.Start(new ProcessStartInfo(_session.LogsDirectoryPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { ShowError("Le dossier des journaux ne peut pas être ouvert."); }
    }

    private void OpenSessionClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_session.DirectoryPath is { } path)
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { ShowError("Le dossier de session ne peut pas être ouvert. Son chemin est affiché dans le journal."); }
    }

    public async Task ShutdownAsync()
    {
        _localShuttingDown = true; PublishLocalState();
        _updateLifetime.Cancel(); _updateCancellation?.Cancel();
        if (_updateTask is { } updateTask) await updateTask;
        _updateHttp.Dispose();
        _activeEditor?.Close(); _accountLifetime.Cancel();
        if (_previewTask is { } previewTask) await previewTask;
        if (_accountMaintenanceTask is { } maintenance) await maintenance;
        if (_accountPreparationTask is { IsCompleted: false } preparation)
            try { await preparation; } catch (Exception ex) when (ex is OperationCanceledException || Controls.PlatformEditor.IsConnectionError(ex)) { }
        await _relayOperationTask;
        await StopLocalServerAsync();
        _hotkeys?.Dispose();
        var stop = _session.StopAsync(); Refresh();
        await stop;
        _refresh.Stop(); _closed = true;
        _destinations.Clear();
        await _session.DisposeAsync();
    }
}
