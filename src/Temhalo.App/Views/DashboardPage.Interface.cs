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
    private InterfacePreferences _preferences = new();
    private int _overlayStyle, _previewState = 2;
    private bool _loadingOverlay, _selectingSettings, _dialogOpen, _uiReady;
    private nint _windowHandle;

    private void Navigate(string tag)
        => Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>().Single(item => (string)item.Tag == tag);
    private void ShowOverlayClicked(object sender, RoutedEventArgs e) => Navigate("overlay");
    private void ShowJournalClicked(object sender, RoutedEventArgs e) { Navigate("settings"); SelectSettings(3); }
    private void SettingsNavigationChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    { if (!_selectingSettings && sender.SelectedItem is not null) SelectSettings(sender.Items.IndexOf(sender.SelectedItem)); }
    private void CompactSettingsChanged(object sender, SelectionChangedEventArgs e)
    { if (!_selectingSettings && SettingsNavigation is not null) SelectSettings(CompactSettingsNavigation.SelectedIndex); }
    private void SelectSettings(int index)
    {
        if (UsageView is null || index is < 0 or > 3) return;
        _selectingSettings = true;
        SettingsNavigation.SelectedItem = SettingsNavigation.Items[index]; CompactSettingsNavigation.SelectedIndex = index;
        UsageView.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        HotkeysView.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        ContinuityView.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        PrivacyView.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        _selectingSettings = false;
    }

    private void RenderPresets()
    {
        if (DirectPresets is null) return;
        DirectPresets.Children.Clear(); SettingsPresets.Children.Clear();
        foreach (int value in _preferences.Presets)
        {
            var button = new ToggleButton { Content = value == 0 ? "0 s · Direct" : $"{value} s", Tag = value };
            AutomationProperties.SetName(button, $"Appliquer {value} secondes");
            button.Click += (_, _) => { Vm.RequestTarget(value); UpdatePresetSelection(); Refresh(); };
            DirectPresets.Children.Add(button);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, MinHeight = 36 };
            row.Children.Add(new TextBlock { Text = value == 0 ? "Direct" : $"{value} s", VerticalAlignment = VerticalAlignment.Center });
            if (value > 0)
            {
                var remove = new Button { Content = "×", Padding = new Thickness(4), MinWidth = 36, MinHeight = 36 };
                AutomationProperties.SetName(remove, $"Retirer le preset {value} secondes");
                remove.Click += async (_, _) => await ChangePresetsAsync(_preferences.Presets.Where(item => item != value).ToArray());
                row.Children.Add(remove);
            }
            SettingsPresets.Children.Add(new Border { Child = row, Padding = new Thickness(12, 4, 4, 4),
                CornerRadius = new CornerRadius(8), Background = Brush("TemhaloSurfaceBrush") });
        }
        var custom = new ToggleButton { Content = "Personnalisé", Tag = -1 };
        custom.Click += (_, _) => { UpdatePresetSelection(); DelayNumber.Focus(FocusState.Programmatic); };
        DirectPresets.Children.Add(custom); UpdatePresetSelection();
    }
    private void UpdatePresetSelection()
    {
        if (DirectPresets is null) return;
        foreach (var button in DirectPresets.Children.OfType<ToggleButton>())
            button.IsChecked = (int)button.Tag == Vm.TargetSeconds || (int)button.Tag == -1 && !_preferences.Presets.Contains(Vm.TargetSeconds);
    }
    private async void AddPresetClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsBusy) return;
        try
        {
            int value = ReadInteger(NewPreset.Value, 1, 300, "Choisissez un nombre entier de 1 à 300 secondes.");
            if (_preferences.Presets.Contains(value)) throw new ArgumentException("Ce preset existe déjà.");
            if (_preferences.Presets.Length >= 8) throw new ArgumentException("Conservez au maximum huit presets.");
            await ChangePresetsAsync(_preferences.Presets.Append(value).Order().ToArray());
        }
        catch (ArgumentException ex) { PresetError.Text = ex.Message; }
    }
    private async Task ChangePresetsAsync(int[] values)
    {
        if (_settingsBusy) return;
        var updated = _preferences with { Presets = values };
        if (await SavePreferencesAsync(updated, includeOverlay: false)) { RenderPresets(); PresetError.Text = ""; }
    }
    private async Task<bool> SavePreferencesAsync(InterfacePreferences preferences, bool includeOverlay = true)
    {
        if (_settingsBusy) return false;
        _settingsBusy = true; Refresh();
        try
        {
            var updated = _settings with { Interface = includeOverlay ? preferences :
                (_settings.Interface ?? new()) with { Presets = preferences.Presets } };
            await _settingsStore.SaveAsync(updated); _settings = updated; _preferences = preferences;
            _session.RecordControlEvent("Préférences d’interface enregistrées."); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { ShowError(SecretRedactor.Shared.Redact(ex.Message)); return false; }
        finally { _settingsBusy = false; Refresh(); }
    }

    private OverlayAppearance ReadOverlay() => new() { Position = OverlayPosition.SelectedIndex,
        ScalePercent = (int)Math.Round(OverlayScale.Value), Accent = OverlayAccent.Text.Trim(),
        Label = OverlayLabel.Text, VisibleWhenLive = OverlayVisibleLive.IsOn };
    private void LoadOverlayAppearance()
    {
        if (!_uiReady || OverlayStage is null) return;
        _loadingOverlay = true;
        var value = _preferences.Appearance(_overlayStyle);
        OverlayStyle.SelectedIndex = _overlayStyle; OverlayPosition.SelectedIndex = value.Position;
        SignatureChoice.IsChecked = _overlayStyle == 0; RepereChoice.IsChecked = _overlayStyle == 1;
        FilChoice.IsChecked = _overlayStyle == 2; ChronoChoice.IsChecked = _overlayStyle == 3;
        OverlayScale.Value = value.ScalePercent; OverlayAccent.Text = value.Accent;
        OverlayLabel.Text = value.Label; OverlayVisibleLive.IsOn = value.VisibleWhenLive;
        _loadingOverlay = false; UpdateOverlay();
    }
    private bool UpdateOverlay()
    {
        if (!_uiReady || _loadingOverlay || OverlayStage is null || OverlayLabel is null || OverlayVisibleLive is null) return false;
        try
        {
            var value = ReadOverlay(); value.Validate();
            _preferences = (_preferences.WithAppearance(_overlayStyle, value)) with { OverlayStyle = _overlayStyle };
            OverlayStage.Render(value, _overlayStyle, _previewState); OverlayError.Text = ""; return true;
        }
        catch (ArgumentException ex) { OverlayError.Text = ex.Message; return false; }
    }
    private void OverlayStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _loadingOverlay || OverlayStage is null || OverlayStyle.SelectedIndex < 0) return;
        _overlayStyle = OverlayStyle.SelectedIndex; LoadOverlayAppearance();
    }
    private void OverlayChoiceClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsBusy) return;
        _overlayStyle = int.Parse((string)((FrameworkElement)sender).Tag); LoadOverlayAppearance();
    }
    private void OverlaySelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateOverlay();
    private void OverlayTextChanged(object sender, TextChangedEventArgs e) => UpdateOverlay();
    private void OverlayVisibilityChanged(object sender, RoutedEventArgs e) => UpdateOverlay();
    private void OverlayScaleChanged(object sender, RangeBaseValueChangedEventArgs e) => UpdateOverlay();
    private void PreviewStateClicked(object sender, RoutedEventArgs e)
    {
        _previewState = int.Parse((string)((FrameworkElement)sender).Tag);
        PreviewLive.IsChecked = _previewState == 0; PreviewAdding.IsChecked = _previewState == 1;
        PreviewDelayed.IsChecked = _previewState == 2; UpdateOverlay();
    }
    private async void SaveOverlayClicked(object sender, RoutedEventArgs e)
    { if (!_settingsBusy && UpdateOverlay() && await SavePreferencesAsync(_preferences)) ShowInfo("Habillage enregistré", "Les réglages des quatre compositions sont conservés sur cet ordinateur."); }
    private void ResetOverlayClicked(object sender, RoutedEventArgs e)
    {
        if (_settingsBusy) return;
        _preferences = new InterfacePreferences { Presets = _preferences.Presets }; _overlayStyle = 0;
        _previewState = 2; PreviewLive.IsChecked = false; PreviewAdding.IsChecked = false; PreviewDelayed.IsChecked = true;
        LoadOverlayAppearance();
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
        if (RouteDestination is null || OutputMode is null || _session.Running) return;
        int count = _destinations.Count(destination => destination.Enabled);
        string destination = OutputMode.SelectedIndex == 1 ? $"{count} destination(s)" : "Fichier local";
        RouteDestination.Text = OutputMode.SelectedIndex == 1 ? string.Join(" · ", _destinations.Where(value => value.Enabled).Select(value => value.Name)) : "Fichier de test local";
        OutputTitle.Text = "Sortie · " + destination;
        OutputDetail.Text = OutputMode.SelectedIndex == 1 ? "RTMP / RTMPS · copie du flux" : "FLV · H.264 / AAC · copie du flux";
    }

    private async void LocalPreviewClicked(object sender, RoutedEventArgs e)
    {
        if (_session.Running || _starting || _settingsBusy || _destinations.Count >= 4) return;
        var added = new DestinationSettings { Name = "Aperçu VLC", Provider = 3,
            ServerUrl = "rtmp://127.0.0.1:1936/live", StreamKey = "preview" };
        if (await SaveDestinationSetAsync(_destinations.Append(added).ToArray(), switchToNetwork: true))
            ShowInfo("Destination locale prête", "Lancez tools/Start-LocalPreview.ps1, puis ouvrez rtmp://127.0.0.1:1936/live/preview dans VLC. Désactivez vos plateformes si vous souhaitez tester seulement en local.");
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
