using System.Security.Cryptography;
using System.Text;
using Temhalo.Infrastructure.Windows.LocalControl;
using Temhalo.Infrastructure.Windows.LocalWeb;

namespace Temhalo.App.Views;

public sealed partial class DashboardPage
{
    private StreamDeckControlServer? _streamDeck;
    private RelayControlState _relayState = new("Stopped", false, false);

    private RelayControlState ReadRelayControlState()
    {
        bool available = !_closed && !_localShuttingDown && !_settingsBusy && !_starting && !_relayStopping &&
            !_session.Stopping && !_dialogOpen && !_accountMaintenance && !_installingUpdate;
        return new(_starting ? "Starting" : _relayStopping || _session.Stopping ? "Stopping" :
            _session.Running ? "Running" : "Stopped", available && !_session.Running, available && _session.Running);
    }

    private async Task<DelayCommandResult> ApplyStreamDeckCommandAsync(DelayCommand command, CancellationToken token)
    {
        if (command.Action is not ("relay-start" or "relay-stop")) return await ApplyLocalCommandAsync(command, token);
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
        if (_streamDeck is not null || _localShuttingDown) return;
        StreamDeckControlServer? server = null;
        try
        {
            // An isolated validation instance must never attract the user's deck.
            string? name = Environment.GetCommandLineArgs().Contains("--settings-directory")
                ? "Temhalo.StreamDeck.test." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_settingsStore.FilePath)))[..32]
                : null;
            server = new StreamDeckControlServer(() => Volatile.Read(ref _localState), ApplyStreamDeckCommandAsync, name,
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
