using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Core.Recording;

namespace AiMeetingAssistant.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ICaptureSourceDiscovery _sourceDiscovery;
    private readonly RecordingSession _recordingSession;
    private readonly ICaptureCoordinator _captureCoordinator;
    private readonly Stopwatch _recordingStopwatch = new();
    private readonly DispatcherTimer _recordingTimer;
    private Dispatcher? _uiDispatcher;
    private IReadOnlyList<CaptureSource> _screenSources = [];
    private IReadOnlyList<CaptureSource> _systemAudioSources = [];
    private IReadOnlyList<CaptureSource> _microphoneSources = [];
    private CaptureSource? _selectedScreen;
    private CaptureSource? _selectedSystemAudio;
    private CaptureSource? _selectedMicrophone;
    private string? _errorMessage;
    private bool _isDiscoveringSources;
    private double _systemAudioLevel;
    private double _microphoneLevel;
    private string? _statusMessage;

    public MainWindowViewModel(ICaptureSourceDiscovery sourceDiscovery, ICaptureCoordinator captureCoordinator)
    {
        _sourceDiscovery = sourceDiscovery;
        _captureCoordinator = captureCoordinator;
        _recordingSession = new(captureCoordinator);
        _recordingTimer = new(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _recordingTimer.Tick += OnRecordingTimerTick;
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
        RecordingSessionState.Preparing => "Preparing audio streams...",
        RecordingSessionState.Recording => "Recording microphone and system audio",
        RecordingSessionState.Stopping => "Stopping audio streams...",
        RecordingSessionState.Completed => "Recording completed",
        RecordingSessionState.Failed => "Recording failed",
        _ => State.ToString()
    };

    public string RecordingButtonLabel => IsRecording ? "Stop recording" : "Start recording";

    public string RecordingElapsedLabel => _recordingStopwatch.Elapsed.ToString(@"hh\:mm\:ss");

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

    public double SystemAudioLevel
    {
        get => _systemAudioLevel;
        private set { _systemAudioLevel = value; OnPropertyChanged(); }
    }

    public double MicrophoneLevel
    {
        get => _microphoneLevel;
        private set { _microphoneLevel = value; OnPropertyChanged(); }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<PipelineStep> PipelineSteps { get; } =
    [
        new("Discover capture sources", "Sprint 1.2"),
        new("Microphone capture", "Sprint 1.3"),
        new("System audio capture", "Sprint 1.4"),
        new("Screen capture", "Sprint 1.5"),
        new("Synchronized session", "Sprint 1.6")
    ];

    public Dispatcher? UIDispatcher
    {
        get => _uiDispatcher;
        set => _uiDispatcher = value;
    }

    public Task InitializeAsync() => RefreshSourcesAsync();

    public async Task ShutdownAsync()
    {
        _recordingTimer.Stop();
        _recordingStopwatch.Stop();
        try
        {
            await _recordingSession.ShutdownAsync();
        }
        catch
        {
            // Ignore shutdown errors
        }
    }

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

            // Register for live level updates if using RealCaptureCoordinator (Sprint 1.3+)
            if (_captureCoordinator is AiMeetingAssistant.Windows.Capture.RealCaptureCoordinator realCoordinator)
            {
                realCoordinator.SystemAudioLevelChanged += OnSystemAudioLevelChanged;
                realCoordinator.SystemAudioFaulted += OnSystemAudioFaulted;
                realCoordinator.MicrophoneLevelChanged += OnMicrophoneLevelChanged;
                realCoordinator.MicrophoneFaulted += OnMicrophoneFaulted;
            }

            var plan = new CapturePlan(SelectedScreen.Id, SelectedSystemAudio.Id, SelectedMicrophone.Id);
            await _recordingSession.StartAsync(plan);
        }
        catch (Exception exception)
        {
            ErrorMessage = FormatExceptionChain(exception);
        }
    }

    private static string FormatExceptionChain(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (!messages.Contains(current.Message, StringComparer.Ordinal))
                messages.Add(current.Message);
        }
        return string.Join(" → ", messages);
    }

    private void OnSystemAudioLevelChanged(object? sender, AudioFrameCapturedEventArgs eventArgs)
    {
        // Convert RMS dB to a 0-100 scale for UI display
        // Typical range: -80dB to 0dB
        SystemAudioLevel = NormalizeLevel(eventArgs.Level.RmsDb);
    }

    private void OnSystemAudioFaulted(object? sender, AudioCaptureFaultEventArgs eventArgs)
    {
        ErrorMessage = $"System audio error: {eventArgs.ErrorMessage}";
    }

    private void OnMicrophoneLevelChanged(object? sender, AudioFrameCapturedEventArgs eventArgs)
    {
        MicrophoneLevel = NormalizeLevel(eventArgs.Level.RmsDb);
    }

    private void OnMicrophoneFaulted(object? sender, AudioCaptureFaultEventArgs eventArgs)
    {
        ErrorMessage = $"Microphone error: {eventArgs.ErrorMessage}";
    }

    private static double NormalizeLevel(double rmsDb) => Math.Max(0, Math.Min(100, (rmsDb + 80) / 0.8));

    private void OnRecordingStateChanged(object? sender, RecordingStateChangedEventArgs eventArgs)
    {
        // Marshal entire callback to UI thread to ensure thread safety for command updates
        var dispatcher = _uiDispatcher ?? Dispatcher.CurrentDispatcher;
        if (dispatcher.CheckAccess())
        {
            // Already on UI thread
            HandleRecordingStateChanged(eventArgs);
        }
        else
        {
            // Marshal to UI thread
            dispatcher.BeginInvoke(() => HandleRecordingStateChanged(eventArgs));
        }
    }

    private void HandleRecordingStateChanged(RecordingStateChangedEventArgs eventArgs)
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(CanChangeSources));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(RecordingButtonLabel));

        if (eventArgs.CurrentState == RecordingSessionState.Preparing)
        {
            _recordingTimer.Stop();
            _recordingStopwatch.Reset();
            OnPropertyChanged(nameof(RecordingElapsedLabel));
        }
        else if (eventArgs.CurrentState == RecordingSessionState.Recording)
        {
            _recordingStopwatch.Start();
            _recordingTimer.Start();
            OnPropertyChanged(nameof(RecordingElapsedLabel));
        }

        // Reset level when recording stops
        if (eventArgs.CurrentState is RecordingSessionState.Completed or RecordingSessionState.Failed)
        {
            _recordingTimer.Stop();
            _recordingStopwatch.Stop();
            OnPropertyChanged(nameof(RecordingElapsedLabel));
            SystemAudioLevel = 0;
            MicrophoneLevel = 0;
            StatusMessage = eventArgs.ErrorMessage ?? (eventArgs.CurrentState == RecordingSessionState.Completed ? "Recording saved to artifacts/captures/" : "Recording failed");

            // Unregister from level updates
            if (_captureCoordinator is AiMeetingAssistant.Windows.Capture.RealCaptureCoordinator realCoordinator)
            {
                realCoordinator.SystemAudioLevelChanged -= OnSystemAudioLevelChanged;
                realCoordinator.SystemAudioFaulted -= OnSystemAudioFaulted;
                realCoordinator.MicrophoneLevelChanged -= OnMicrophoneLevelChanged;
                realCoordinator.MicrophoneFaulted -= OnMicrophoneFaulted;
            }
        }
        else if (eventArgs.CurrentState == RecordingSessionState.Recording)
        {
            StatusMessage = "Recording in progress...";
        }

        RaiseCommandStates();
    }

    private void OnRecordingTimerTick(object? sender, EventArgs eventArgs) =>
        OnPropertyChanged(nameof(RecordingElapsedLabel));

    private void RaiseCommandStates()
    {
        ToggleRecordingCommand.RaiseCanExecuteChanged();
        RefreshSourcesCommand.RaiseCanExecuteChanged();
    }

    private static CaptureSource? PreserveSelection(CaptureSource? current, IReadOnlyList<CaptureSource> sources) =>
        sources.FirstOrDefault(source => source.Id == current?.Id) ?? sources.FirstOrDefault();

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (PropertyChanged == null)
            return;

        var dispatcher = _uiDispatcher ?? Dispatcher.CurrentDispatcher;
        if (dispatcher.CheckAccess())
        {
            // Already on UI thread
            PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        else
        {
            // Marshal to UI thread
            dispatcher.BeginInvoke(() =>
            {
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }
    }
}

public sealed record PipelineStep(string Name, string Phase);
