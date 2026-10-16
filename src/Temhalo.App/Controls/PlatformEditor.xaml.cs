using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Temhalo.Infrastructure.Windows.Accounts;
using Temhalo.Infrastructure.Windows.Logging;
using Temhalo.Infrastructure.Windows.Settings;

namespace Temhalo.App.Controls;

/// <summary>Transactional editor: saved destinations are never mutated by cancel, provider changes or failed OAuth.</summary>
public sealed partial class PlatformEditor : UserControl
{
    private static readonly int[] ProviderOrder = [0, 1, 2, 4, 5, 6, 3];
    private readonly Dictionary<int, DestinationSettings> _drafts = new();
    private readonly StreamingAccountService _accounts;
    private readonly string _id;
    private ConnectedAccount? _account;
    private CancellationTokenSource? _authorization;
    private int _provider = -1;
    private bool _loading, _closed;
    public bool Busy => _authorization is not null;
    public event Action<bool>? ReadinessChanged;
    public bool CanSave => !Busy && NameInput.Text.Trim().Length > 0 && (_provider > 2 || ManualChoice.IsOn
        ? KeyInput.Password.Length > 0 && ServerInput.Text.Trim().Length > 0
        : _account is { AccessToken.Length: > 0 } account && (account.ExpiresAt > DateTimeOffset.UtcNow || account.RefreshToken.Length > 0) &&
            (_provider != 1 || account.BroadcastId.Length > 0));

    public PlatformEditor(StreamingAccountService accounts, DestinationSettings? existing, int provider = 0)
    {
        _accounts = accounts; _id = existing?.Id ?? Guid.NewGuid().ToString("N");
        InitializeComponent();
        NameInput.TextChanged += (_, _) => ReadinessChanged?.Invoke(CanSave);
        ServerInput.TextChanged += (_, _) => ReadinessChanged?.Invoke(CanSave);
        KeyInput.PasswordChanged += (_, _) => ReadinessChanged?.Invoke(CanSave);
        if (existing is not null) _drafts[existing.Provider] = existing;
        ProviderChoice.SelectedIndex = Array.IndexOf(ProviderOrder, existing?.Provider ?? provider);
    }
    public void Close() { _closed = true; _authorization?.Cancel(); }
    private DestinationSettings ReadDraft() => new()
    {
        Id = _id, Provider = _provider, Name = NameInput.Text.Trim(), Enabled = EnabledChoice.IsChecked == true,
        RememberKey = RememberChoice.IsChecked == true, ServerUrl = ServerInput.Text.Trim(), StreamKey = KeyInput.Password,
        Account = _account
    };
    public DestinationSettings ValidatedValue()
    {
        if (Busy) throw new ArgumentException("Terminez ou annulez la connexion en cours.");
        var draft = ReadDraft();
        if (_provider <= 2 && !ManualChoice.IsOn)
        {
            if (_account is null || _account.AccessToken.Length == 0) throw new ArgumentException("Connectez votre compte ou choisissez la clé manuelle.");
            if (_provider == 1 && _account.BroadcastId.Length == 0) throw new ArgumentException("Choisissez le live YouTube à utiliser.");
            draft = draft with { Account = _account, ServerUrl = "", StreamKey = "" };
        }
        else draft = draft with { Account = null };
        SecretRedactor.Shared.Remember(draft.StreamKey);
        draft.Validate(requireKey: true);
        return draft;
    }
    public void Error(string message) { EditorError.Message = message; EditorError.IsOpen = true; }
    private void ProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProviderChoice.SelectedIndex < 0 || NameInput is null) return;
        _authorization?.Cancel();
        if (_provider >= 0) _drafts[_provider] = ReadDraft();
        _provider = ProviderOrder[ProviderChoice.SelectedIndex];
        var value = _drafts.GetValueOrDefault(_provider) ?? new DestinationSettings
        { Id = _id, Provider = _provider, Name = ProviderName(_provider), ServerUrl = DefaultServer(_provider) };
        _loading = true;
        _account = value.Account; _account?.ProtectDiagnostics();
        NameInput.Text = value.Name; EnabledChoice.IsChecked = value.Enabled; RememberChoice.IsChecked = value.RememberKey;
        ServerInput.Text = value.ServerUrl; KeyInput.Password = value.StreamKey;
        ManualChoice.IsOn = _provider > 2 || value.Account is null && value.StreamKey.Length > 0;
        ProviderDescription.Text = _provider switch
        {
            0 => "Reliez votre chaîne Twitch, ou renseignez votre clé de stream.",
            1 => "Reliez votre chaîne YouTube, puis choisissez le live préparé dans Studio.",
            2 => "Reliez votre compte Kick via le service de connexion Temhalo. Le média part directement vers Kick. La clé manuelle reste disponible.",
            4 => "TikTok doit vous avoir donné accès à un serveur et une clé LIVE. Temhalo ne débloque pas cet accès.",
            5 => "Préparez votre diffusion dans Facebook Live Producer, puis copiez le serveur et la clé.",
            6 => "Copiez le serveur et la clé de votre tableau de bord Restream.",
            _ => "Utilisez le serveur RTMP ou RTMPS et la clé fournis par votre service."
        };
        KeyHelp.Text = _provider switch
        {
            0 => "Twitch → Tableau de bord des créateurs → Paramètres → Stream. Serveur automatique Twitch prérempli ; vous pouvez le modifier.",
            1 => "YouTube Studio → Passer au direct → Paramètres du flux. Le serveur RTMPS est prérempli.",
            2 => "Kick → Tableau de bord des créateurs → Paramètres du stream : serveur et clé.",
            _ => "Collez la clé ici et l’adresse du serveur séparément. Aucune clé n’est placée dans les journaux."
        };
        BroadcastChoice.ItemsSource = null; EditorError.IsOpen = false;
        _loading = false; UpdateState();
        if (_account?.Provider == StreamingProvider.YouTube && _account.BroadcastId.Length > 0)
        {
            BroadcastChoice.ItemsSource = new[] { new YouTubeBroadcast(_account.BroadcastId, _account.BroadcastTitle, "") };
            BroadcastChoice.SelectedIndex = 0;
        }
    }
    internal static string ProviderName(int provider) => provider switch
    { 0 => "Twitch", 1 => "YouTube", 2 => "Kick", 4 => "TikTok", 5 => "Facebook", 6 => "Restream", _ => "Serveur personnalisé" };
    private static string DefaultServer(int provider) => provider switch
    { 0 => StreamingAccountService.TwitchIngestUrl, 1 => "rtmps://a.rtmps.youtube.com:443/live2", _ => "" };
    private void ManualChanged(object sender, RoutedEventArgs e)
    { if (!_loading) { _authorization?.Cancel(); EditorError.IsOpen = false; UpdateState(); } }
    private void UpdateState()
    {
        if (AccountPanel is null) return;
        bool manual = _provider > 2 || ManualChoice.IsOn;
        ManualChoice.Visibility = _provider <= 2 ? Visibility.Visible : Visibility.Collapsed;
        ManualFields.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
        AccountPanel.Visibility = manual ? Visibility.Collapsed : Visibility.Visible;
        AuthorizationWait.Visibility = Busy ? Visibility.Visible : Visibility.Collapsed;
        ConnectButton.IsEnabled = !Busy;
        ConnectButton.Content = _account is null ? "Connecter mon compte ↗" : "Changer / reconnecter le compte ↗";
        AccountProfile.Visibility = _account is null ? Visibility.Collapsed : Visibility.Visible;
        AccountProfile.Text = _account is null ? "" : _account.DisplayName + (_account.AccessToken.Length > 0 || _account.RefreshToken.Length > 0 ? " · Compte lié" : " · À reconnecter");
        AccountNote.Visibility = _account is null ? Visibility.Collapsed : Visibility.Visible;
        YouTubePanel.Visibility = _account?.Provider == StreamingProvider.YouTube ? Visibility.Visible : Visibility.Collapsed;
        RememberChoice.Content = manual ? "Mémoriser la clé chiffrée pour mon compte Windows" : "Mémoriser la connexion chiffrée pour mon compte Windows";
        ReadinessChanged?.Invoke(CanSave);
    }
    private async void ConnectClicked(object sender, RoutedEventArgs e)
    {
        if (Busy || _closed) return;
        var attempt = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        _authorization = attempt; int provider = _provider; EditorError.IsOpen = false; UpdateState();
        var progress = new Progress<AuthorizationPrompt>(prompt => DispatcherQueue.TryEnqueue(() =>
        { if (_authorization == attempt && !_closed && _provider == provider) AuthorizationMessage.Text = prompt.Message; }));
        try
        {
            var connected = await _accounts.ConnectAsync((StreamingProvider)provider, progress, attempt.Token);
            if (_closed || _authorization != attempt || _provider != provider || attempt.IsCancellationRequested) return;
            // A failed new connection leaves the original account intact.
            _account = connected; BroadcastChoice.ItemsSource = null;
            if (provider == 1) await LoadBroadcastsAsync(attempt.Token);
        }
        catch (OperationCanceledException) { if (!_closed && _provider == provider && !ManualChoice.IsOn) Error("Connexion interrompue. Vous pouvez réessayer ou utiliser une clé manuelle."); }
        catch (Exception ex) when (IsConnectionError(ex)) { if (!_closed && _provider == provider) Error(SafeError(ex)); }
        finally { if (_authorization == attempt) _authorization = null; attempt.Dispose(); if (!_closed) UpdateState(); }
    }
    private async Task LoadBroadcastsAsync(CancellationToken token)
    {
        if (_account is null) return;
        var current = _account;
        // Do not rotate a persisted refresh token in a cancellable draft. Reconnect explicitly instead.
        if (current.ExpiresAt <= DateTimeOffset.UtcNow) throw new AccountConnectionException("Reconnectez le compte pour actualiser les lives.", true);
        var broadcasts = await _accounts.BroadcastsAsync(current, token);
        if (_closed || token.IsCancellationRequested || _account != current) return;
        BroadcastChoice.ItemsSource = broadcasts;
        BroadcastChoice.SelectedItem = broadcasts.FirstOrDefault(item => item.Id == current.BroadcastId);
        if (broadcasts.Count == 0) Error("Aucun live associé à un flux. Préparez-le dans YouTube Studio, puis actualisez, ou utilisez la clé manuelle.");
    }
    private async void ReloadBroadcastsClicked(object sender, RoutedEventArgs e)
    {
        if (Busy || _closed) return;
        using var attempt = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        _authorization = attempt; UpdateState();
        try { await LoadBroadcastsAsync(attempt.Token); }
        catch (OperationCanceledException) { if (!_closed) Error("Actualisation interrompue. Réessayez."); }
        catch (Exception ex) when (IsConnectionError(ex)) { if (!_closed) Error(SafeError(ex)); }
        finally { if (_authorization == attempt) _authorization = null; if (!_closed) UpdateState(); }
    }
    private void BroadcastChanged(object sender, SelectionChangedEventArgs e)
    { if (!_loading && _account is not null) _account = _account with { BroadcastId = (BroadcastChoice.SelectedItem as YouTubeBroadcast)?.Id ?? "", BroadcastTitle = (BroadcastChoice.SelectedItem as YouTubeBroadcast)?.Title ?? "" }; ReadinessChanged?.Invoke(CanSave); }
    private void CancelConnectionClicked(object sender, RoutedEventArgs e) => _authorization?.Cancel();
    public static bool IsConnectionError(Exception ex) => ex is AccountConnectionException or HttpRequestException or IOException or
        System.ComponentModel.Win32Exception or ArgumentException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException;
    public static string SafeError(Exception ex) => ex is AccountConnectionException ? ex.Message : "La plateforme est indisponible ou sa réponse est inattendue. Réessayez, ou utilisez une clé manuelle.";
}
