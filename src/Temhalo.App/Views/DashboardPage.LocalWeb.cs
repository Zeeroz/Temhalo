using Microsoft.UI.Xaml;
using Temhalo.Infrastructure.Windows.LocalWeb;
using Temhalo.Infrastructure.Windows.Logging;
using Temhalo.Infrastructure.Windows.Settings;
using Windows.ApplicationModel.DataTransfer;

namespace Temhalo.App.Views;

public sealed partial class DashboardPage
{
    private LocalControlServer? _localServer;
    private readonly CancellationTokenSource _localLifetime = new();
    private LocalViewState _localState = new(false, false, 0, null, "Stopped", false, [0, 15, 30, 60], 300, []);
    private bool _localShuttingDown;

    private async Task InitializeLocalServerAsync()
    {
        if (_localShuttingDown) return;
        ObsDockEnabled.IsOn = _settings.ObsDockEnabled;
        LocalServerPort.Value = _settings.LocalAccess?.Port ?? 43120;
        if (!_settings.ObsDockEnabled) { LocalServerStatus.Text = "Désactivé · indépendant du Stream Deck"; DockAddress.Text = "Dock OBS désactivé"; return; }
        try
        {
            if (_settings.LocalAccess is null)
            {
                var updated = _settings with { LocalAccess = new(), Destinations = Persistable(_destinations) };
                await _settingsStore.SaveAsync(updated, _localLifetime.Token); _settings = updated;
            }
            if (_localShuttingDown) return;
            var access = _settings.LocalAccess!;
            SecretRedactor.Shared.Remember(access.DockToken);
            LocalServerPort.Value = access.Port;
            var server = new LocalControlServer(access, () => Volatile.Read(ref _localState), ApplyLocalCommandAsync);
            _localServer = server;
            await server.StartAsync(_localLifetime.Token);
            LocalServerStatus.Text = $"Actif · 127.0.0.1:{access.Port} · dock OBS";
            DockAddress.Text = $"http://127.0.0.1:{access.Port}/dock · accès privé";
            _session.RecordControlEvent("Serveur navigateur local démarré ; liens privés exclus du journal.");
        }
        catch (OperationCanceledException) when (_localShuttingDown) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            if (_localServer is not null) await _localServer.DisposeAsync();
            _localServer = null; LocalServerStatus.Text = "Serveur local indisponible. Choisissez un port libre dans Confidentialité.";
            DockAddress.Text = "Serveur indisponible";
            ShowError("L’accès navigateur n’a pas démarré. Le relais média reste disponible. Vérifiez le port local.");
        }
    }
    private void PublishLocalState()
    {
        var snapshot = _session.Snapshot;
        PublicOutputState[] publicOutputs = _session.Running && snapshot?.Outputs is { Count: > 0 } outputs
            ? outputs.Select(output => new PublicOutputState(output.Id, output.Name, output.Ready,
                output.Reconnecting, output.ExtraDelayMilliseconds)).ToArray()
            : OutputMode.SelectedIndex == 0 ? [new("primary", "Fichier local", false, false, 0)]
            : _destinations.Where(destination => destination.Enabled).Select(destination =>
                new PublicOutputState(destination.Id, destination.Name, false, false, 0)).ToArray();
        Volatile.Write(ref _localState, new(_session.Running, !_settingsBusy && !_starting && !_relayStopping && !_session.Stopping && !_localShuttingDown,
            Vm.TargetSeconds, _session.Running ? snapshot?.Delay?.LastMediaAgeMilliseconds : null,
            snapshot?.Delay?.Control.Phase ?? "Stopped", _session.Running && snapshot?.IngestConnected == true,
            _preferences.Presets, _settings.Hotkeys.ProtectionSeconds,
            publicOutputs));
        Volatile.Write(ref _relayState, ReadRelayControlState());
        _streamDeck?.PublishState();
    }
    private async Task<DelayCommandResult> ApplyLocalCommandAsync(DelayCommand command, CancellationToken token)
    {
        var completion = new TaskCompletionSource<DelayCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var canceled = token.Register(() => completion.TrySetCanceled(token));
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (token.IsCancellationRequested) return;
            if (_closed || _localShuttingDown || _settingsBusy || _starting || _relayStopping || _session.Stopping)
            { completion.TrySetResult(new(false, Vm.TargetSeconds, "Commande indisponible pendant cette opération.")); return; }
            switch (command.Action)
            {
                case "set": Vm.RequestTarget(command.Seconds!.Value); break;
                case "adjust": Vm.AdjustTarget(command.Seconds!.Value); break;
                case "protect": Vm.RequestTarget(_settings.Hotkeys.ProtectionSeconds); break;
                default: completion.TrySetResult(new(false, Vm.TargetSeconds, "Commande inconnue.")); return;
            }
            _session.RecordControlEvent($"Contrôle local : cible demandée {Vm.TargetSeconds} s."); Refresh();
            completion.TrySetResult(new(true, Vm.TargetSeconds, $"Cible appliquée : {Vm.TargetSeconds} s."));
        })) return new(false, 0, "L’interface n’est plus disponible.");
        return await completion.Task.ConfigureAwait(false);
    }
    private void CopyDockClicked(object sender, RoutedEventArgs e) => CopyPrivateLink(_localServer?.DockUrl);
    private void CopyPrivateLink(string? value)
    {
        if (string.IsNullOrEmpty(value)) { ShowInfo("Accès OBS", "Démarrez le serveur local depuis Réglages → Confidentialité."); return; }
        var data = new DataPackage(); data.SetText(value); Clipboard.SetContent(data);
        ShowInfo("Lien privé copié", "Collez-le dans OBS sur cet ordinateur. Il reste valable après le redémarrage de Temhalo.");
    }
    private async void SaveLocalServerClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsBusy || _localShuttingDown) return;
        _settingsBusy = true; Refresh();
        try
        {
            var access = (_settings.LocalAccess ?? new()) with { Port = ReadInteger(LocalServerPort.Value, 1024, 65535, "Choisissez un port entier entre 1024 et 65535.") };
            access.Validate(); var updated = _settings with { LocalAccess = access, ObsDockEnabled = ObsDockEnabled.IsOn };
            await _settingsStore.SaveAsync(updated); _settings = updated;
            if (_localServer is not null) await _localServer.DisposeAsync();
            _localServer = null; await InitializeLocalServerAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { ShowError(ex.Message); }
        finally { _settingsBusy = false; Refresh(); }
    }
    private async Task StopLocalServerAsync()
    {
        _localShuttingDown = true; await _localLifetime.CancelAsync();
        if (_streamDeck is not null) { await _streamDeck.DisposeAsync(); _streamDeck = null; }
        if (_localServer is not null) { await _localServer.DisposeAsync(); _localServer = null; }
        _localLifetime.Dispose();
    }
}
