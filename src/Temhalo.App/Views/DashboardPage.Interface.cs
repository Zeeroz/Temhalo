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
            button.Click += (_, _) => { Vm.RequestTarget(value); UpdatePresetSelection(); Refresh(); };
            DirectPresets.Children.Add(button);

        }
        var custom = new ToggleButton { Content = "Personnalisé", Tag = -1 };
        custom.Click += (_, _) => { UpdatePresetSelection(); DelayNumber.Focus(FocusState.Programmatic); };
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

    private async void ObsGuideClicked(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            var body = new StackPanel { Spacing = 12 };
            body.Children.Add(new TextBlock { Text = "OBS → Paramètres → Stream → Service personnalisé.\nServeur : rtmp://127.0.0.1:1935/live\nClé locale : stream\nH.264 / AAC · CBR · Images clés : 1 s", TextWrapping = TextWrapping.Wrap });
            var copy = new Button { Content = "Copier l’adresse du serveur" };
            copy.Click += (_, _) => { var data = new DataPackage(); data.SetText("rtmp://127.0.0.1:1935/live"); Clipboard.SetContent(data); };
            body.Children.Add(copy);
            body.Children.Add(new TextBlock { Text = "Démarrez le relais avant la publication OBS. La vérification automatique via obs-websocket sera raccordée ensuite.", TextWrapping = TextWrapping.Wrap });
            await new ContentDialog { XamlRoot = XamlRoot, Title = "Configurer l’entrée OBS", Content = body, CloseButtonText = "Compris" }.ShowAsync();
        }
        finally { _dialogOpen = false; }
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
