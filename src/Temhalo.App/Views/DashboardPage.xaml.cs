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
    private bool _settingsBusy = true, _settingsLoaded;
    private CancellationTokenSource? _relayStartCancellation;
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

    public void AttachWindow(nint handle) => _windowHandle = handle;

    private async Task InitializeSettingsAsync()
    {
        try
        {
            _settings = (await _settingsStore.LoadAsync()).ForDesktop();
            SecretRedactor.Shared.Remember(_settings.StreamKey);
            MemoryLimit.Value = _settings.MemoryLimitMiB;
            InitializeDestinations();
            RenderPresets(); UpdateDestinationLabels();
            if (_setupGuiding) Navigate("diffusion");
            _session.RecordControlEvent("Configuration chargée ; aucun démarrage automatique de diffusion.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { ShowError(SecretRedactor.Shared.Redact(ex.Message)); }
        finally { _settingsBusy = false; await InitializeStreamDeckAsync(); Refresh(); }
    }

    private static int ReadInteger(double value, int minimum, int maximum, string error)
        => double.IsFinite(value) && value >= minimum && value <= maximum && value == Math.Truncate(value)
            ? (int)value : throw new ArgumentException(error);

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
        // The second header column must release its width when commands move below navigation.
        HeaderGrid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : GridLength.Auto;
        HeaderGrid.Padding = tiny ? new Thickness(12, 8, 12, 12) : new Thickness(20, 8, 28, 12);
        foreach (var (layout, label, button) in new[]
        { (PlatformsStepLayout, PlatformsStepLabel, PlatformsStep), (ObsStepLayout, ObsStepLabel, ObsStep), (LaunchStepLayout, LaunchStepLabel, LaunchStep) })
        {
            layout.ColumnDefinitions[0].Width = tiny ? new GridLength(1, GridUnitType.Star) : new GridLength(28);
            layout.ColumnDefinitions[1].Width = tiny ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(label, tiny ? 0 : 1); Grid.SetRow(label, tiny ? 1 : 0);
            label.Margin = tiny ? new Thickness(0, 8, 0, 0) : new Thickness(0);
            label.FontSize = tiny ? 12 : 14;
            button.Padding = tiny ? new Thickness(4, 8, 4, 8) : new Thickness(16, 10, 16, 10);
        }
        string state = ActualWidth - 56 >= 920 ? "WideLayout" : "CompactLayout";
        if (_layout == state) return;
        _layout = state;
        DelayGrid.ColumnDefinitions[1].Width = state == "WideLayout" ? new GridLength(1.9, GridUnitType.Star) : new GridLength(0);
        VisualStateManager.GoToState(this, state, false);
    }

    private void NavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (DashboardView is null) return;
        string tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "direct";
        if (tag != "diffusion" && _activeEditor is not null && !_editorSaving) CancelEditor();
        DashboardView.Visibility = tag == "direct" ? Visibility.Visible : Visibility.Collapsed;
        DestinationView.Visibility = tag == "diffusion" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        RefreshSetup();
        MainScroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    private async void StartStopClicked(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        if (!_session.Running && !_starting && !HasPreparedOutput())
        { _setupGuiding = true; Navigate("diffusion"); ShowSetupStep(1); return; }
        var state = ReadRelayControlState();
        if (!(state.CanStart || state.CanStop)) return;
        bool start = !_session.Running;
        if (!start && !await ConfirmStopAsync()) return;
        state = ReadRelayControlState();
        if (!(start ? state.CanStart : state.CanStop)) return;
        await (_relayOperationTask = ChangeRelayRunningAsync(start));
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
            _setupGuiding = false; Navigate("direct");
            Refresh();
            if (!double.IsFinite(MemoryLimit.Value) || MemoryLimit.Value is < 1 or > 4096 || Math.Truncate(MemoryLimit.Value) != MemoryLimit.Value)
                throw new ArgumentException("La limite mémoire doit être un entier de 1 à 4096 Mio.");
            using var preparation = CancellationTokenSource.CreateLinkedTokenSource(_accountLifetime.Token);
            _relayStartCancellation = preparation;
            preparation.CancelAfter(TimeSpan.FromMinutes(2));
            _accountPreparationTask = ReadOutputsAsync(preparation.Token);
            var outputs = await _accountPreparationTask;
            await _session.StartAsync(Vm.TargetSeconds, checked((long)MemoryLimit.Value * 1048576), outputs, _accountLifetime.Token, preparation.Token);
            OutputTitle.Text = $"Sorties · {outputs.Count} destination(s)";
            OutputDetail.Text = "RTMP / RTMPS · passthrough";
            _shownError = null;
        }
        catch (Temhalo.Infrastructure.Windows.Accounts.AccountConnectionException ex) { ShowError(ex.Message); }
        catch (Exception ex) when (ex is KeyNotFoundException or FormatException) { ShowError("La plateforme a renvoyé une réponse inattendue. Reconnectez le compte ou utilisez une clé manuelle."); }
        catch (OperationCanceledException) when (_shuttingDown || _closed) { }
        catch (OperationCanceledException) { ShowError("La préparation des outils a été interrompue ou a dépassé son délai. Réessayez Démarrer."); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or UnauthorizedAccessException or HttpRequestException)
        { ShowError(SecretRedactor.Shared.Redact(ex.Message)); }
        finally { _relayStartCancellation = null; _starting = false; RenderDestinations(); Refresh(); }
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
        if (_starting) Vm.CanEdit = false;
        DestinationFields.IsEnabled = !_session.Running && !_starting && !_settingsBusy && !_accountMaintenance;
        var relayControl = ReadRelayControlState();
        StartStopButton.IsEnabled = relayControl.CanStart || relayControl.CanStop;
        ObsAcknowledged.IsEnabled = !_settingsBusy && !_starting;
        RefreshSetup(); MaintainTwitchAccounts();
        StartStopButton.Content = _starting ? "Démarrage…" : _relayStopping || _session.Stopping ? "Vidange en cours…" :
            _session.Running ? "Arrêter le relais" : HasPreparedOutput() ? "Démarrer le relais" : "Configurer la diffusion";
        LastEvent.Text = snapshot?.State == "RetryingOutput" ? "Reconnexion automatique de la sortie. Le buffer RAM est conservé." :
            _session.LastAction ?? snapshot?.LastEvent ?? "Prêt. Démarrez le relais, puis lancez la publication OBS.";
        IngestState.Foreground = Brush(snapshot?.IngestConnected == true && _session.Running ? "TemhaloSuccessBrush" : "TemhaloMutedBrush");
        OutputState.Foreground = Brush(snapshot?.OutputReady == true && _session.Running ? "TemhaloSuccessBrush" : "TemhaloMutedBrush");
        BufferBar.Foreground = Brush(Vm.BufferWarning ? "TemhaloWarningBrush" : "TemhaloControlBrush");
        if (SettingsView.Visibility == Visibility.Visible && JournalExpander.IsExpanded) RateChart.Update(Vm.Rates.Points);
        RefreshDirect(snapshot);
        ReserveValue.Text = $"{snapshot?.Delay?.BufferedTags ?? 0:N0} tags FLV";
        ReconnectValue.Text = (snapshot?.Outputs is { Count: > 1 } outputs ? outputs.Sum(output => Math.Max(0, output.Attempt - 1)) : snapshot?.Delay?.OutputReconnections ?? 0).ToString();
        SkippedValue.Text = $"{(snapshot?.Delay?.SkippedMediaTags ?? 0) + (snapshot?.Outputs?.Sum(output => output.SkippedTags) ?? 0):N0} tags FLV";
        PublishStreamDeckState(); RefreshStreamDeckStatus();
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

    public async Task ShutdownAsync()
    {
        _shuttingDown = true; PublishStreamDeckState();
        _updateLifetime.Cancel(); _updateCancellation?.Cancel();
        if (_updateTask is { } updateTask) await updateTask;
        _updateHttp.Dispose();
        _activeEditor?.Close(); _accountLifetime.Cancel();
        if (_accountMaintenanceTask is { } maintenance) await maintenance;
        if (_accountPreparationTask is { IsCompleted: false } preparation)
            try { await preparation; } catch (Exception ex) when (ex is OperationCanceledException || Controls.PlatformEditor.IsConnectionError(ex)) { }
        await _relayOperationTask;
        await StopStreamDeckAsync();
        var stop = _session.StopAsync(); Refresh();
        await stop;
        _refresh.Stop(); _closed = true;
        _destinations.Clear();
        await _session.DisposeAsync();
    }
}
