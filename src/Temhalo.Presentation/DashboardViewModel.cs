using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Temhalo.Core.Buffering;

namespace Temhalo.Presentation;

/// <summary>UI-thread owned editor and projection; telemetry never overwrites an unfinished edit.</summary>
public partial class DashboardViewModel : ObservableObject
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private readonly Action<int> _request;
    private double _numberDraft, _sliderDraft;
    private int _target;
    public BitrateHistory Rates { get; } = new();
    public int ConfiguredMemoryLimitMiB { get; set; } = 512;

    public DashboardViewModel(Action<int> request) => _request = request;

    public double NumberDraft
    {
        get => _numberDraft;
        set
        {
            if (!SetProperty(ref _numberDraft, value)) return;
            DraftError = !double.IsFinite(value) || value < 0 || value > 300 || value != Math.Truncate(value)
                ? "Saisissez un nombre entier de 0 à 300 secondes." : "";
            if (DraftError.Length == 0 && SetProperty(ref _sliderDraft, value, nameof(SliderDraft))) { }
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }
    public double SliderDraft
    {
        get => _sliderDraft;
        set { if (SetProperty(ref _sliderDraft, value)) NumberDraft = Math.Round(value); }
    }
    public int TargetSeconds => _target;
    public string TargetText => FormatDelay(_target * 1000L);
    public bool CanApply => DraftError.Length == 0 && CanEdit;
    [ObservableProperty] private string _draftError = "";
    [ObservableProperty] private bool _canEdit = true;
    [ObservableProperty] private string _currentText = "—";
    [ObservableProperty] private string _transitionText = "En attente du flux OBS";
    [ObservableProperty] private string _sessionText = "Relais arrêté";
    [ObservableProperty] private string _ingestText = "Déconnecté";
    [ObservableProperty] private string _outputText = "Arrêtée";
    [ObservableProperty] private string _inputRate = "—";
    [ObservableProperty] private string _outputRate = "—";
    [ObservableProperty] private string _liveDuration = "00:00:00";
    [ObservableProperty] private string _bufferText = "0 / 512 Mio";
    [ObservableProperty] private double _bufferPercent;
    [ObservableProperty] private string _bufferHint = "Le buffer se remplit avec le flux OBS.";
    [ObservableProperty] private string _historyText = "Relecture · en attente du flux OBS";
    [ObservableProperty] private bool _bufferWarning;
    [ObservableProperty] private bool _preset0 = true;
    [ObservableProperty] private bool _preset15;
    [ObservableProperty] private bool _preset30;
    [ObservableProperty] private bool _preset60;
    [ObservableProperty] private bool _presetCustom;

    partial void OnCanEditChanged(bool value) => ApplyCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply() => SetTarget((int)NumberDraft);

    [RelayCommand]
    private void Preset(string value)
    {
        if (!CanEdit || !int.TryParse(value, out int seconds) || seconds is < 0 or > 300) return;
        NumberDraft = seconds;
        SetTarget(seconds);
    }

    public void RequestTarget(int seconds)
    {
        if (!CanEdit) return;
        if (seconds is < 0 or > 300) throw new ArgumentOutOfRangeException(nameof(seconds));
        NumberDraft = seconds;
        SetTarget(seconds);
    }
    public void AdjustTarget(int change) => RequestTarget(Math.Clamp(_target + change, 0, 300));

    private void SetTarget(int seconds)
    {
        _request(seconds);
        _target = seconds;
        NotifyTarget();
    }

    private void NotifyTarget()
    {
        OnPropertyChanged(nameof(TargetSeconds)); OnPropertyChanged(nameof(TargetText));
        Preset0 = _target == 0; Preset15 = _target == 15; Preset30 = _target == 30; Preset60 = _target == 60;
        PresetCustom = _target is not (0 or 15 or 30 or 60);
        // A repeated preset may have toggled a native button before the same target was requested.
        foreach (string property in new[] { nameof(Preset0), nameof(Preset15), nameof(Preset30), nameof(Preset60), nameof(PresetCustom) })
            OnPropertyChanged(property);
    }

    public void Refresh(FixedDelaySnapshot? delay, bool running, bool stopping, bool ingestConnected,
        bool outputReady, bool network, string state, int attempt, long outputBytes, double elapsedSeconds,
        TimeSpan liveDuration)
    {
        CanEdit = !stopping;
        SessionText = stopping ? "Vidange en cours" : running ? "Relais actif" : "Relais arrêté";
        IngestText = ingestConnected && running ? "Connecté" : "Déconnecté";
        OutputText = !running ? "Arrêtée" : state == "RetryingOutput" ? "Reconnexion en cours" : outputReady ? (network ? "Émission confirmée" : "Écriture active") :
            delay is { EmittedMediaTags: > 0 } ? "Émission en attente" : "En attente du média";
        LiveDuration = $"{(long)liveDuration.TotalHours:00}:{liveDuration.Minutes:00}:{liveDuration.Seconds:00}";
        Rates.Sample(elapsedSeconds, delay?.InputBytes ?? 0, outputBytes, attempt, running);
        InputRate = running ? Rates.InputMbps.ToString("0.00", French) + " Mbit/s" : "—";
        OutputRate = running ? Rates.OutputMbps.ToString("0.00", French) + " Mbit/s" : "—";
        if (delay is null)
        {
            CurrentText = "—"; TransitionText = running ? "En attente du flux OBS" : "Relais arrêté";
            BufferPercent = 0; BufferWarning = false; BufferText = $"0 / {ConfiguredMemoryLimitMiB} MiB";
            BufferHint = "Le buffer se remplit avec le flux OBS.";
            HistoryText = "Relecture · en attente du flux OBS";
            return;
        }
        if (_target != delay.TargetSeconds) { _target = delay.TargetSeconds; NotifyTarget(); }
        // During a pause this remains the last observed packet age, never a synthetic measurement.
        CurrentText = running && delay.LastMediaAgeMilliseconds.HasValue ? FormatDelay(delay.LastMediaAgeMilliseconds.Value) : "—";
        TransitionText = !running ? state == "Faulted" ? "Session interrompue" : "Relais arrêté" : stopping ? "Vidange du buffer" :
            state == "RetryingOutput" ? "Sortie interrompue · buffer conservé" :
            delay.RecoveryDelayMilliseconds > 0 ? "Connexion rétablie · retard conservé, réappliquez une cible pour rattraper" :
            delay.InputCompleted && delay.BufferedTags > 0 ? "OBS déconnecté · vidange du buffer" :
            delay.InputCompleted && delay.ReadTags > 0 ? "En attente de reconnexion OBS" :
            delay.ReadTags == 0 ? "En attente du flux OBS" :
            delay.Control.Phase switch
            {
                "IncreasingPause" => "Historique insuffisant · pause pour compléter le délai",
                "IncreasingPlayback" => "Ajout du délai · reprise intermédiaire",
                "IncreasingAwaitingKeyframe" => "Ajout du délai · attente d’image clé",
                "ReplayingKeyframe" => "Relecture · reprise sur image clé",
                "SeekingKeyframe" => "Réduction · attente d’image clé",
                "ResumingKeyframe" => "Reprise sur image clé",
                "OutputBacklog" => delay.TargetSeconds == 0
                    ? "Sortie ralentie · retard résiduel, cliquez sur 0 s pour rattraper"
                    : "Sortie ralentie · retard supérieur à la cible",
                "TargetUnreachedAtInputEnd" => "Source terminée · cible non atteinte",
                _ => delay.Control.TargetReached ? "Délai atteint" : "Remplissage du buffer"
            };
        BufferWarning = delay.BufferWarning;
        HistoryText = !running ? "Relecture · historique actif pendant le relais" :
            $"Relecture · {FormatDelay(delay.HistoryAvailableMilliseconds)} d’historique disponible";
        BufferPercent = delay.MaximumBufferBytes == 0 ? 0 : 100.0 * delay.BufferBytes / delay.MaximumBufferBytes;
        BufferText = $"{delay.BufferBytes / 1048576.0:0.#} / {delay.MaximumBufferBytes / 1048576} MiB";
        BufferHint = delay.BufferWarning ? "Buffer proche de la limite mémoire" : state == "RetryingOutput"
            ? "Entrée maintenue tant que la limite RAM le permet." : delay.InputCompleted && delay.BufferedTags > 0
            ? "OBS déconnecté. Le buffer continue de se vider." : "Mémoire du buffer et de l’historique de relecture";
    }

    private static string FormatDelay(long milliseconds) => TimeSpan.FromMilliseconds(milliseconds).ToString(@"mm\:ss", CultureInfo.InvariantCulture);
}
