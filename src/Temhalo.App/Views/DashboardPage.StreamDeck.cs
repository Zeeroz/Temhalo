using System.Security.Cryptography;
using System.Text;
using Temhalo.Infrastructure.Windows.LocalControl;

namespace Temhalo.App.Views;

public sealed partial class DashboardPage
{
    private StreamDeckControlServer? _streamDeck;
    private bool _shuttingDown;
    private LocalViewState _controlState = new(false, false, 0, null, "Stopped", false, DelayPresets, 300, []);

    private void PublishStreamDeckState()
    {
        var snapshot = _session.Snapshot;
        PublicOutputState[] outputs = _session.Running && snapshot?.Outputs is { Count: > 0 } active
            ? active.Select(output => new PublicOutputState(output.Id, output.Name, output.Ready,
                output.Reconnecting, output.ExtraDelayMilliseconds)).ToArray()
            : _destinations.Where(destination => destination.Enabled).Select(destination =>
                new PublicOutputState(destination.Id, destination.Name, false, false, 0)).ToArray();
        Volatile.Write(ref _controlState, new(_session.Running,
            !_settingsBusy && !_starting && !_relayStopping && !_session.Stopping && !_shuttingDown,
            Vm.TargetSeconds, _session.Running ? snapshot?.Delay?.LastMediaAgeMilliseconds : null,
            snapshot?.Delay?.Control.Phase ?? "Stopped", _session.Running && snapshot?.IngestConnected == true,
            DelayPresets, 300, outputs));
        Volatile.Write(ref _relayState, ReadRelayControlState());
        _streamDeck?.PublishState();
    }

    private async Task<DelayCommandResult> ApplyDelayCommandAsync(DelayCommand command, CancellationToken token)
    {
        var completion = new TaskCompletionSource<DelayCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var canceled = token.Register(() => completion.TrySetCanceled(token));
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (token.IsCancellationRequested) return;
            if (_closed || _shuttingDown || _settingsBusy || _starting || _relayStopping || _session.Stopping)
            { completion.TrySetResult(new(false, Vm.TargetSeconds, "Commande indisponible pendant cette opération.")); return; }
            switch (command.Action)
            {
                case "set": Vm.RequestTarget(command.Seconds!.Value); break;
                case "adjust": Vm.AdjustTarget(command.Seconds!.Value); break;
                case "protect": Vm.RequestTarget(300); break;
                default: completion.TrySetResult(new(false, Vm.TargetSeconds, "Commande inconnue.")); return;
            }
            _session.RecordControlEvent($"Stream Deck : cible demandée {Vm.TargetSeconds} s."); Refresh();
            completion.TrySetResult(new(true, Vm.TargetSeconds, $"Cible appliquée : {Vm.TargetSeconds} s."));
        })) return new(false, 0, "Interface indisponible.");
        return await completion.Task.ConfigureAwait(false);
    }

    private async Task StopStreamDeckAsync()
    {
        if (_streamDeck is not null) { await _streamDeck.DisposeAsync(); _streamDeck = null; }
    }

    private void InstallStreamDeckClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        string package = Path.Combine(AppContext.BaseDirectory, "plugins", "streamdeck", "Temhalo.streamDeckPlugin");
        if (!File.Exists(package))
        { ShowInfo("Plugin Stream Deck", "Le plugin est fourni avec la version installée de Temhalo. Il est également disponible dans les téléchargements GitHub."); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(package) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        { ShowError("Installez l’application Elgato Stream Deck, puis réessayez l’installation du plugin."); }
    }

    private RelayControlState _relayState = new("Stopped", false, false);

    private RelayControlState ReadRelayControlState()
    {
        bool available = !_closed && !_shuttingDown && !_settingsBusy && !_starting && !_relayStopping &&
            !_session.Stopping && !_dialogOpen && !_accountMaintenance && !_installingUpdate;
        return new(_starting ? "Starting" : _relayStopping || _session.Stopping ? "Stopping" :
            _session.Running ? "Running" : "Stopped", available && !_session.Running, available && _session.Running);
    }

    private async Task<DelayCommandResult> ApplyStreamDeckCommandAsync(DelayCommand command, CancellationToken token)
    {
        if (command.Action is not ("relay-start" or "relay-stop")) return await ApplyDelayCommandAsync(command, token);
        var completion = new TaskCompletionSource<DelayCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var canceled = token.Register(() => completion.TrySetCanceled(token));
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (token.IsCancellationRequested) return;
            var state = ReadRelayControlState();
            bool start = command.Action == "relay-start";
            if (!(start ? state.CanStart : state.CanStop))
            { completion.TrySetResult(new(false, Vm.TargetSeconds, "Relais occupé ou état déjà modifié.")); return; }
            // Acknowledge acceptance immediately. Tool/OAuth preparation and shutdown can
            // outlast the IPC deadline; their actual state is pushed independently.
            _session.RecordControlEvent(start ? "Stream Deck : démarrage du relais demandé." : "Stream Deck : arrêt du relais demandé.");
            _relayOperationTask = ChangeRelayRunningAsync(start);
            completion.TrySetResult(new(true, Vm.TargetSeconds, start ? "Démarrage demandé." : "Arrêt demandé."));
        })) return new(false, Vm.TargetSeconds, "Interface indisponible.");
        return await completion.Task.ConfigureAwait(false);
    }
    private async Task InitializeStreamDeckAsync()
    {
        if (_streamDeck is not null || _shuttingDown) return;
        StreamDeckControlServer? server = null;
        try
        {
            // An isolated validation instance must never attract the user's deck.
            string? name = Environment.GetCommandLineArgs().Contains("--settings-directory")
                ? "Temhalo.StreamDeck.test." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_settingsStore.FilePath)))[..32]
                : null;
            server = new StreamDeckControlServer(() => Volatile.Read(ref _controlState), ApplyStreamDeckCommandAsync, name,
                () => Volatile.Read(ref _relayState));
            await server.StartAsync(); _streamDeck = server;
            _session.RecordControlEvent("Connexion directe Stream Deck prête.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (server is not null) await server.DisposeAsync();
            StreamDeckStatus.Text = "Connexion Stream Deck indisponible. Vérifiez qu’une autre instance de Temhalo n’est pas ouverte.";
        }
    }
    private void RefreshStreamDeckStatus()
    {
        if (_streamDeck is not null)
            StreamDeckStatus.Text = !_streamDeck.Running ? "Connexion Stream Deck indisponible" : _streamDeck.ConnectedClients > 0
                ? "Stream Deck connecté directement à Temhalo" : "Prêt · en attente du plugin Stream Deck";
    }
}
