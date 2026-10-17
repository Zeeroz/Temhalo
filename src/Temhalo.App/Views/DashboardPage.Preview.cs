using System.Diagnostics;
using Microsoft.UI.Xaml;
using Temhalo.Infrastructure.Windows.Preview;
using Temhalo.Infrastructure.Windows.Settings;

namespace Temhalo.App.Views;

public sealed partial class DashboardPage
{
    private bool _openingPreview;
    private Task? _previewTask;

    private (DestinationSettings Destination, LocalPreviewTarget Target)? PreviewOutput()
    {
        if (OutputMode.SelectedIndex != 1) return null;
        var candidates = _destinations.Where(d => d.Enabled && d.Account is null)
            .Select(d => (Destination: d, Target: LocalPreviewTarget.TryCreate(d.ServerUrl, d.StreamKey)))
            .Where(value => value.Target is not null).ToArray();
        var selected = candidates.FirstOrDefault(value => _session.Snapshot?.Outputs?.Any(output => output.Id == value.Destination.Id && output.Ready) == true);
        if (selected.Target is null) selected = candidates.FirstOrDefault();
        return selected.Target is null ? null : (selected.Destination, selected.Target);
    }

    private void RefreshPreview()
    {
        var preview = PreviewOutput();
        OpenVlcButton.IsEnabled = !_openingPreview && !_starting && !_settingsBusy && !_session.Stopping &&
            _session.Running && _session.Snapshot?.IngestConnected == true && preview is not null;
        OpenVlcButton.Content = _openingPreview ? "Ouverture de VLC…" : "Ouvrir l’aperçu VLC";
        PreviewHint.Text = _openingPreview ? "Préparation de la lecture locale…" : preview is null
            ? "Pour un essai privé, ajoutez une destination RTMP locale dans Diffusion."
            : !_session.Running ? "Démarrez le relais, puis lancez le flux OBS."
            : _session.Snapshot?.IngestConnected != true ? "En attente du flux OBS."
            : $"Sortie locale · {preview.Value.Destination.Name}";
    }

    private async void OpenVlcClicked(object sender, RoutedEventArgs e)
    {
        if (!OpenVlcButton.IsEnabled || PreviewOutput() is not { } preview) return;
        _previewTask = OpenPreviewAsync(preview.Destination.Id, preview.Target);
        await _previewTask;
    }

    private async Task OpenPreviewAsync(string outputId, LocalPreviewTarget target)
    {
        _openingPreview = true; RefreshPreview();
        try
        {
            string? executable = await Task.Run(LocalPreviewTarget.FindVlc, _accountLifetime.Token);
            if (executable is null)
            { ShowInfo("VLC introuvable", "Installez VLC sur ce PC, puis réessayez Ouvrir l’aperçu VLC."); return; }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_accountLifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await _session.EnsurePreviewReceiverAsync(target, timeout.Token);
            while (_session.Running && !_session.Stopping &&
                _session.Snapshot?.Outputs?.Any(output => output.Id == outputId && output.Ready) != true)
                await Task.Delay(100, timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            if (!_session.Running || _session.Stopping) return;
            using var process = Process.Start(target.VlcStartInfo(executable));
            if (process is null) throw new InvalidOperationException("VLC ne démarre pas.");
            _session.RecordControlEvent("Aperçu VLC ouvert avec le profil local de faible latence.");
        }
        catch (OperationCanceledException) when (_closed || _accountLifetime.IsCancellationRequested || !_session.Running || _session.Stopping) { }
        catch (OperationCanceledException)
        { ShowInfo("Aperçu en attente", "La sortie locale n’est pas prête. Vérifiez le flux OBS et la destination dans Diffusion, puis réessayez."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { ShowInfo("Aperçu indisponible", "VLC ou le récepteur local n’a pas pu démarrer. Vérifiez la destination locale et réessayez."); _session.RecordControlEvent("Ouverture de l’aperçu VLC interrompue."); }
        finally { _openingPreview = false; if (!_closed) RefreshPreview(); }
    }
}
