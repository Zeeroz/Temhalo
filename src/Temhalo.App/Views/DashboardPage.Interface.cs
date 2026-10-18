using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Temhalo.Infrastructure.Windows.Logging;
using Temhalo.Infrastructure.Windows.Settings;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Temhalo.App.Views;

public sealed partial class DashboardPage
{
    private static readonly int[] DelayPresets = [0, 15, 30, 60];
    private bool _dialogOpen;
    private nint _windowHandle;

    private void Navigate(string tag)
        => Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>().Single(item => (string)item.Tag == tag);
    private void ShowJournalClicked(object sender, RoutedEventArgs e)
    { Navigate("settings"); JournalExpander.IsExpanded = true; JournalExpander.StartBringIntoView(); }

    private void RenderPresets()
    {
        if (DirectPresets is null) return;
        DirectPresets.Children.Clear();
        foreach (int value in DelayPresets)
        {
            var button = new ToggleButton { Content = value == 0 ? "0 s · Direct" : $"{value} s", Tag = value };
            AutomationProperties.SetName(button, $"Appliquer {value} secondes");
            AutomationProperties.SetAutomationId(button, "Preset" + value);
            button.Click += (_, _) => { Vm.RequestTarget(value); UpdatePresetSelection(); Refresh(); };
            DirectPresets.Children.Add(button);

        }
        var custom = new ToggleButton { Content = "Personnalisé", Tag = -1 };
        AutomationProperties.SetAutomationId(custom, "CustomDelay");
        custom.Click += (_, _) =>
        {
            CustomDelayEditor.Visibility = CustomDelayEditor.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            UpdatePresetSelection();
            if (CustomDelayEditor.Visibility == Visibility.Visible) DelayNumber.Focus(FocusState.Programmatic);
        };
        DirectPresets.Children.Add(custom); UpdatePresetSelection();
    }
    private void UpdatePresetSelection()
    {
        if (DirectPresets is null) return;
        foreach (var button in DirectPresets.Children.OfType<ToggleButton>())
            button.IsChecked = (int)button.Tag == Vm.TargetSeconds || (int)button.Tag == -1 && !DelayPresets.Contains(Vm.TargetSeconds);
    }
    private async void SaveMemoryClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsBusy) return;
        _settingsBusy = true; Refresh();
        try
        {
            var updated = _settings with { MemoryLimitMiB = ReadInteger(MemoryLimit.Value, 1, 4096, "La limite doit être un entier de 1 à 4096 MiB.") };
            await _settingsStore.SaveAsync(updated); _settings = updated;
            MemoryStatus.Text = "Limite enregistrée pour le prochain démarrage du relais.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { ShowError(ex.Message); }
        finally { _settingsBusy = false; Refresh(); }
    }
    private void UpdateDestinationLabels()
    {
        if (RouteDestination is null || _session.Running) return;
        var enabled = _destinations.Where(value => value.Enabled).ToArray();
        RouteDestination.Text = string.Join(" · ", enabled.Select(value => value.Name));
        OutputTitle.Text = $"Sortie · {enabled.Length} destination(s)";
        OutputDetail.Text = "RTMP / RTMPS · copie du flux";
    }

    private void ObsGuideClicked(object sender, RoutedEventArgs e)
    { if (_editorSaving) return; CancelEditor(); _setupGuiding = true; Navigate("diffusion"); ShowSetupStep(2); }

    private void ConfigureClicked(object sender, RoutedEventArgs e)
    { _setupGuiding = true; Navigate("diffusion"); ShowSetupStep(1); }

    private void ManagePlatformsClicked(object sender, RoutedEventArgs e) => Navigate("diffusion");

    private async Task<bool> ConfirmStopAsync()
    {
        _dialogOpen = true;
        try
        {
            return await new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "Arrêter le relais ?",
                Content = "L’émission vers toutes les plateformes de Temhalo sera interrompue. OBS reste ouvert. Vous pourrez redémarrer le relais quand vous le souhaiterez.",
                PrimaryButtonText = "Arrêter le relais", CloseButtonText = "Continuer la diffusion",
                DefaultButton = ContentDialogButton.Close, RequestedTheme = ElementTheme.Dark
            }.ShowAsync() == ContentDialogResult.Primary;
        }
        finally { _dialogOpen = false; Refresh(); }
    }

    private void RelayHintClicked(object sender, RoutedEventArgs e)
    {
        if (_starting) { _relayStartCancellation?.Cancel(); return; }
        if (Vm.BufferWarning) { ShowJournalClicked(sender, e); MemoryLimit.StartBringIntoView(); return; }
        ObsGuideClicked(sender, e);
    }

    private void RefreshDirect(Temhalo.Infrastructure.Windows.Processes.LocalRelaySnapshot? snapshot)
    {
        bool hasDestination = _destinations.Any(destination => destination.Enabled);
        DirectEmpty.Visibility = hasDestination ? Visibility.Collapsed : Visibility.Visible;
        DirectContent.Visibility = hasDestination ? Visibility.Visible : Visibility.Collapsed;
        DirectEmptyTitle.Text = _destinations.Count == 0 ? "Préparez votre première diffusion." : "Aucune plateforme active.";
        DirectEmptyDescription.Text = _destinations.Count == 0
            ? "Ajoutez une plateforme, puis reliez OBS. Vos choix seront conservés pour vos prochains lives."
            : "Activez une plateforme dans Diffusion pour démarrer le relais. Vos réglages OBS sont conservés.";
        LiveClock.Visibility = _session.Running ? Visibility.Visible : Visibility.Collapsed;
        SessionLabel.Text = _starting ? "Préparation…" : _relayStopping || _session.Stopping ? "Vidange en cours" : _session.Running && snapshot?.IngestConnected != true
            ? "En attente d’OBS" : Vm.SessionText;
        IngestState.Text = _session.Running && snapshot?.IngestConnected == true ? "●  Flux reçu" : _session.Running ? "En attente du flux" : "Non connecté";
        PreparationProgress.Visibility = _starting ? Visibility.Visible : Visibility.Collapsed;
        string? message = null;
        RelayHint.Severity = InfoBarSeverity.Informational;
        RelayHintButton.Visibility = Visibility.Visible;
        if (_starting)
        {
            RelayHint.Title = "Préparation de Temhalo";
            message = "Vérification des outils et des destinations. Les outils manquants sont téléchargés au besoin.";
            RelayHintButton.Content = "Annuler";
        }
        else if (_relayStopping || _session.Stopping)
        {
            RelayHint.Title = "Arrêt du relais en cours";
            message = "Temhalo termine la vidange du média en attente.";
            RelayHintButton.Visibility = Visibility.Collapsed;
        }
        else if (Vm.BufferWarning)
        {
            RelayHint.Title = "La mémoire réservée au flux est presque pleine.";
            message = "Le délai disponible peut être limité. Consultez la limite mémoire.";
            RelayHint.Severity = InfoBarSeverity.Warning; RelayHintButton.Content = "Voir la mémoire";
        }
        else if (_session.Running && snapshot?.IngestConnected != true)
        {
            RelayHint.Title = "Temhalo attend le flux OBS.";
            message = snapshot?.Delay is { ReadTags: > 0 }
                ? "OBS s’est déconnecté. Temhalo vide le média en attente, puis attend la reconnexion."
                : "Dans OBS, cliquez sur « Commencer le streaming ». L’émission vers vos plateformes démarrera à la réception du flux.";
            RelayHintButton.Content = "Revoir le guide OBS";
        }
        else if (_session.Running && snapshot?.Outputs?.Any(output => output.Reconnecting) == true)
        {
            RelayHint.Title = "Une sortie est interrompue.";
            message = "Temhalo tente de rétablir la connexion. Consultez l’état de chaque plateforme ci-dessous.";
            RelayHint.Severity = InfoBarSeverity.Warning; RelayHintButton.Visibility = Visibility.Collapsed;
        }
        RelayHint.IsOpen = message is not null;
        RelayHint.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        RelayHint.Message = message ?? "";
    }
    private void ShowInfo(string title, string message)
    { Notice.Severity = InfoBarSeverity.Informational; Notice.Title = title; Notice.Message = message; Notice.Visibility = Visibility.Visible; Notice.IsOpen = true; MainScroll.ChangeView(null, 0, null, true); }
    private async void ExportLogsClicked(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen || _windowHandle == 0) return;
        _dialogOpen = true;
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = $"Temhalo-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}" };
            picker.FileTypeChoices.Add("Journal texte", new[] { ".txt" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
            var file = await picker.PickSaveFileAsync();
            if (file is not null) await File.WriteAllLinesAsync(file.Path, _journal.Select(SecretRedactor.Shared.Redact));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { ShowError("Le journal n’a pas pu être exporté."); }
        finally { _dialogOpen = false; }
    }
}
