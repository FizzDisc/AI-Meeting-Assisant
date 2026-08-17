using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Core.Recording;

namespace AiMeetingAssistant.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ICaptureSourceDiscovery _sourceDiscovery;
    private readonly RecordingSession _recordingSession;
    private IReadOnlyList<CaptureSource> _screenSources = [];
    private IReadOnlyList<CaptureSource> _systemAudioSources = [];
    private IReadOnlyList<CaptureSource> _microphoneSources = [];
    private CaptureSource? _selectedScreen;
    private CaptureSource? _selectedSystemAudio;
    private CaptureSource? _selectedMicrophone;
    private string? _errorMessage;
    private bool _isDiscoveringSources;

    public MainWindowViewModel(ICaptureSourceDiscovery sourceDiscovery, ICaptureCoordinator captureCoordinator)
    {
        _sourceDiscovery = sourceDiscovery;
        _recordingSession = new(captureCoordinator);
        ToggleRecordingCommand = new AsyncRelayCommand(ToggleRecordingAsync, CanToggleRecording);
        RefreshSourcesCommand = new AsyncRelayCommand(RefreshSourcesAsync, () => CanChangeSources);
        _recordingSession.StateChanged += OnRecordingStateChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand ToggleRecordingCommand { get; }

    public AsyncRelayCommand RefreshSourcesCommand { get; }

    public RecordingSessionState State => _recordingSession.State;

    public bool IsRecording => State is RecordingSessionState.Recording;

    public bool CanChangeSources => !_isDiscoveringSources && State is RecordingSessionState.Idle
        or RecordingSessionState.Completed
        or RecordingSessionState.Failed;

    public string StateLabel => State switch
    {
        RecordingSessionState.Idle when IsDiscoveringSources => "Discovering Windows devices...",
        RecordingSessionState.Idle => "Ready",
        RecordingSessionState.Preparing => "Preparing simulation...",
        RecordingSessionState.Recording => "Recording simulation active",
        RecordingSessionState.Stopping => "Stopping simulation...",
        RecordingSessionState.Completed => "Simulation completed",
        RecordingSessionState.Failed => "Simulation failed",
        _ => State.ToString()
    };

    public string RecordingButtonLabel => IsRecording ? "Stop simulation" : "Start simulation";

    public string SourceSummary => IsDiscoveringSources
        ? "Scanning Windows devices..."
        : $"{ScreenSources.Count} display(s) · {SystemAudioSources.Count} output(s) · {MicrophoneSources.Count} microphone(s)";

    public bool IsDiscoveringSources
    {
        get => _isDiscoveringSources;
        private set
        {
            _isDiscoveringSources = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanChangeSources));
            OnPropertyChanged(nameof(StateLabel));
            OnPropertyChanged(nameof(SourceSummary));
            RaiseCommandStates();
        }
    }

    public IReadOnlyList<CaptureSource> ScreenSources
    {
        get => _screenSources;
        private set { _screenSources = value; OnPropertyChanged(); OnPropertyChanged(nameof(SourceSummary)); }
    }

    public IReadOnlyList<CaptureSource> SystemAudioSources
    {
        get => _systemAudioSources;
        private set { _systemAudioSources = value; OnPropertyChanged(); OnPropertyChanged(nameof(SourceSummary)); }
    }

    public IReadOnlyList<CaptureSource> MicrophoneSources
    {
        get => _microphoneSources;
        private set { _microphoneSources = value; OnPropertyChanged(); OnPropertyChanged(nameof(SourceSummary)); }
    }

    public CaptureSource? SelectedScreen
    {
        get => _selectedScreen;
        set { _selectedScreen = value; OnPropertyChanged(); ToggleRecordingCommand.RaiseCanExecuteChanged(); }
    }

    public CaptureSource? SelectedSystemAudio
    {
        get => _selectedSystemAudio;
        set { _selectedSystemAudio = value; OnPropertyChanged(); ToggleRecordingCommand.RaiseCanExecuteChanged(); }
    }

    public CaptureSource? SelectedMicrophone
    {
        get => _selectedMicrophone;
        set { _selectedMicrophone = value; OnPropertyChanged(); ToggleRecordingCommand.RaiseCanExecuteChanged(); }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set { _errorMessage = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<PipelineStep> PipelineSteps { get; } =
    [
        new("Discover capture sources", "Sprint 1.2"),
        new("Microphone capture", "Sprint 1.3"),
        new("System audio capture", "Sprint 1.4"),
        new("Screen capture", "Sprint 1.5"),
        new("Synchronized session", "Sprint 1.6")
    ];

    public Task InitializeAsync() => RefreshSourcesAsync();

    private async Task RefreshSourcesAsync()
    {
        IsDiscoveringSources = true;
        ErrorMessage = null;
        try
        {
            var sources = await _sourceDiscovery.DiscoverAsync();
            ScreenSources = sources.Where(source => source.Kind is CaptureSourceKind.Screen).ToArray();
            SystemAudioSources = sources.Where(source => source.Kind is CaptureSourceKind.SystemAudio).ToArray();
            MicrophoneSources = sources.Where(source => source.Kind is CaptureSourceKind.Microphone).ToArray();

            SelectedScreen = PreserveSelection(SelectedScreen, ScreenSources);
            SelectedSystemAudio = PreserveSelection(SelectedSystemAudio, SystemAudioSources);
            SelectedMicrophone = PreserveSelection(SelectedMicrophone, MicrophoneSources);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Device discovery failed: {exception.Message}";
        }
        finally
        {
            IsDiscoveringSources = false;
        }
    }

    private bool CanToggleRecording() => IsRecording ||
        (CanChangeSources && SelectedScreen is not null && SelectedSystemAudio is not null && SelectedMicrophone is not null);

    private async Task ToggleRecordingAsync()
    {
        ErrorMessage = null;
        try
        {
            if (IsRecording)
            {
                await _recordingSession.StopAsync();
                return;
            }

            if (SelectedScreen is null || SelectedSystemAudio is null || SelectedMicrophone is null)
            {
                throw new InvalidOperationException("Select one display, output, and microphone first.");
            }

            var plan = new CapturePlan(SelectedScreen.Id, SelectedSystemAudio.Id, SelectedMicrophone.Id);
            await _recordingSession.StartAsync(plan);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private void OnRecordingStateChanged(object? sender, RecordingStateChangedEventArgs eventArgs)
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(CanChangeSources));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(RecordingButtonLabel));
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        ToggleRecordingCommand.RaiseCanExecuteChanged();
        RefreshSourcesCommand.RaiseCanExecuteChanged();
    }

    private static CaptureSource? PreserveSelection(CaptureSource? current, IReadOnlyList<CaptureSource> sources) =>
        sources.FirstOrDefault(source => source.Id == current?.Id) ?? sources.FirstOrDefault();

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));
}

public sealed record PipelineStep(string Name, string Phase);
