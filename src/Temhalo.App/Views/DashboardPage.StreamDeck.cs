using System.Security.Cryptography;
using System.Text;
using Temhalo.Infrastructure.Windows.LocalControl;

namespace Temhalo.App.Views;

public sealed partial class DashboardPage
{
    private StreamDeckControlServer? _streamDeck;
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
            server = new StreamDeckControlServer(() => Volatile.Read(ref _localState), ApplyLocalCommandAsync, name);
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
