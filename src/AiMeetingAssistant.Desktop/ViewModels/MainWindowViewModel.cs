using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Core.Recording;
using AiMeetingAssistant.Windows.Worker;

namespace AiMeetingAssistant.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ICaptureSourceDiscovery _sourceDiscovery;
    private readonly RecordingSession _recordingSession;
    private readonly ICaptureCoordinator _captureCoordinator;
    private readonly PythonWorkerClient? _workerClient;
    private readonly string? _modelPath;
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
    private string? _latestSessionDirectory;
    private CancellationTokenSource? _transcriptionCancellation;
    private bool _isTranscribing;
    private double _transcriptionProgress;
    private string _transcriptionStatusMessage = "Complete a recording to enable local transcription.";
    private string? _transcriptPath;

    public MainWindowViewModel(ICaptureSourceDiscovery sourceDiscovery, ICaptureCoordinator captureCoordinator,
        PythonWorkerClient? workerClient = null, string? modelPath = null)
    {
        _sourceDiscovery = sourceDiscovery;
        _captureCoordinator = captureCoordinator;
        _workerClient = workerClient;
        _modelPath = modelPath;
        _recordingSession = new(captureCoordinator);
        _recordingTimer = new(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _recordingTimer.Tick += OnRecordingTimerTick;
        ToggleRecordingCommand = new AsyncRelayCommand(ToggleRecordingAsync, CanToggleRecording);
        RefreshSourcesCommand = new AsyncRelayCommand(RefreshSourcesAsync, () => CanChangeSources);
        TranscribeLatestCommand = new AsyncRelayCommand(TranscribeLatestAsync, () => CanTranscribeLatest);
        CancelTranscriptionCommand = new AsyncRelayCommand(CancelTranscriptionAsync, () => IsTranscribing);
        _recordingSession.StateChanged += OnRecordingStateChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand ToggleRecordingCommand { get; }

    public AsyncRelayCommand RefreshSourcesCommand { get; }
    public AsyncRelayCommand TranscribeLatestCommand { get; }
    public AsyncRelayCommand CancelTranscriptionCommand { get; }

    public RecordingSessionState State => _recordingSession.State;

    public bool IsRecording => State is RecordingSessionState.Recording;

    public bool CanChangeSources => !_isDiscoveringSources && !IsTranscribing && State is RecordingSessionState.Idle
        or RecordingSessionState.Completed
        or RecordingSessionState.Failed;

    public bool CanTranscribeLatest => !IsTranscribing && _workerClient is not null &&
        _latestSessionDirectory is not null && Directory.Exists(_latestSessionDirectory) &&
        _modelPath is not null && Directory.Exists(_modelPath);

    public string StateLabel => IsTranscribing ? "Transcribing latest recording" : State switch
    {
        RecordingSessionState.Idle when IsDiscoveringSources => "Discovering Windows devices...",
        RecordingSessionState.Idle => "Ready",
        RecordingSessionState.Preparing => "Preparing video and audio streams...",
        RecordingSessionState.Recording => "Recording screen and audio",
        RecordingSessionState.Stopping => "Finalizing video and audio...",
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

    public bool IsTranscribing
    {
        get => _isTranscribing;
        private set
        {
            _isTranscribing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanTranscribeLatest));
            OnPropertyChanged(nameof(CanChangeSources));
            OnPropertyChanged(nameof(StateLabel));
            RaiseCommandStates();
        }
    }

    public double TranscriptionProgress
    {
        get => _transcriptionProgress;
        private set { _transcriptionProgress = value; OnPropertyChanged(); }
    }

    public string TranscriptionStatusMessage
    {
        get => _transcriptionStatusMessage;
        private set { _transcriptionStatusMessage = value; OnPropertyChanged(); }
    }

    public string? TranscriptPath
    {
        get => _transcriptPath;
        private set { _transcriptPath = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<PipelineStep> PipelineSteps { get; } =
    [
        new("Capture foundation", "Complete"),
        new("Local transcription", "Sprint 2.5"),
        new("Speaker diarization", "Sprint 3"),
        new("Meeting intelligence", "Sprint 4"),
        new("Knowledge base", "Later")
    ];

    public Dispatcher? UIDispatcher
    {
        get => _uiDispatcher;
        set => _uiDispatcher = value;
    }

    public async Task InitializeAsync()
    {
        CaptureRecoveryReport? recovery = null;
        if (_captureCoordinator is AiMeetingAssistant.Windows.Capture.CombinedCaptureCoordinator combined)
            recovery = combined.RecoverInterruptedSessions();
        await RefreshSourcesAsync();
        if (recovery?.RecoveredSessions > 0) StatusMessage = $"Recovered {recovery.RecoveredSessions} interrupted recording(s).";
        if (recovery?.Issues.Count > 0) ErrorMessage = $"Session recovery found {recovery.Issues.Count} issue(s): {recovery.Issues[0]}";
        if (_workerClient is not null)
        {
            try
            {
                var health = await _workerClient.CheckHealthAsync();
                StatusMessage = health.MlReady
                    ? $"AI runtime ready · {health.Diagnostics.Compute.Mode.ToUpperInvariant()}/{health.Diagnostics.Compute.ComputeType} · Python {health.PythonVersion}"
                    : health.RuntimeSupported
                        ? $"AI worker connected · setup required: {string.Join(", ", health.Diagnostics.MissingRequirements)}"
                        : $"AI worker connected · Python {health.PythonVersion} is unsupported; use Python 3.10 through 3.13.";
                if (_modelPath is null)
                    TranscriptionStatusMessage = "No local model installed. Use the future Model Manager or development installer.";
            }
            catch (Exception exception)
            {
                ErrorMessage = $"AI worker unavailable: {exception.Message}";
            }
        }
    }

    public async Task ShutdownAsync()
    {
        _transcriptionCancellation?.Cancel();
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
        if (_workerClient is not null)
        {
            try { await _workerClient.DisposeAsync(); } catch { }
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
        (!IsTranscribing && CanChangeSources && SelectedScreen is not null && SelectedSystemAudio is not null && SelectedMicrophone is not null);

    private async Task TranscribeLatestAsync()
    {
        if (!CanTranscribeLatest || _workerClient is null || _latestSessionDirectory is null || _modelPath is null)
            return;

        ErrorMessage = null;
        TranscriptPath = null;
        TranscriptionProgress = 0;
        IsTranscribing = true;
        _transcriptionCancellation = new CancellationTokenSource();
        var token = _transcriptionCancellation.Token;
        string? activeJobId = null;
        try
        {
            var microphone = Directory.GetFiles(_latestSessionDirectory, "microphone_*.wav").SingleOrDefault();
            var systemAudio = Directory.GetFiles(_latestSessionDirectory, "system_audio_*.wav").SingleOrDefault();
            if (microphone is null || systemAudio is null)
                throw new InvalidDataException("The latest session does not contain exactly one microphone and system-audio WAV file.");

            var outputPath = Path.Combine(_latestSessionDirectory, "processing", "transcript.json");
            TranscriptionStatusMessage = "Queuing local transcription...";
            var job = await _workerClient.StartTranscriptionAsync([microphone, systemAudio], _modelPath, outputPath,
                computePreference: "automatic", cancellationToken: token);
            activeJobId = job.JobId;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                job = await _workerClient.GetTranscriptionStatusAsync(job.JobId, token);
                TranscriptionProgress = Math.Clamp(job.Progress * 100, 0, 100);
                TranscriptionStatusMessage = FormatTranscriptionStatus(job);
                if (job.Status == "completed")
                {
                    TranscriptPath = job.OutputPath ?? outputPath;
                    break;
                }
                if (job.Status == "failed") throw new InvalidOperationException(job.Error ?? "Local transcription failed.");
                if (job.Status == "cancelled") break;
                await Task.Delay(500, token);
            }
        }
        catch (OperationCanceledException)
        {
            TranscriptionStatusMessage = "Cancelling local transcription...";
            if (activeJobId is not null)
            {
                try
                {
                    var cancelled = await _workerClient.CancelTranscriptionAsync(activeJobId, CancellationToken.None);
                    TranscriptionStatusMessage = FormatTranscriptionStatus(cancelled);
                }
                catch (Exception exception)
                {
                    ErrorMessage = $"Transcription cancellation failed: {exception.Message}";
                }
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Transcription failed: {exception.Message}";
            TranscriptionStatusMessage = "Transcription failed.";
        }
        finally
        {
            _transcriptionCancellation?.Dispose();
            _transcriptionCancellation = null;
            IsTranscribing = false;
        }
    }

    private Task CancelTranscriptionAsync()
    {
        _transcriptionCancellation?.Cancel();
        return Task.CompletedTask;
    }

    private static string FormatTranscriptionStatus(TranscriptionJobStatus job) => job.Status switch
    {
        "queued" => "Transcription queued...",
        "normalizing" => "Mixing and normalizing audio...",
        "loading-model" => "Loading local speech model...",
        "transcribing" => $"Transcribing {FormatSource(job.Source)} on {job.Device?.ToUpperInvariant() ?? "local hardware"}...",
        "completed" => $"Transcription completed · {job.SegmentCount ?? 0} segment(s)",
        "cancelled" => "Transcription cancelled.",
        _ => job.Status
    };

    private static string FormatSource(string? source) => source switch
    {
        "microphone" => "microphone",
        "system_audio" => "system audio",
        _ => "audio"
    };

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
                throw new InvalidOperationException("Select one display, output, and microphone first.");

            if (_captureCoordinator is AiMeetingAssistant.Windows.Capture.CombinedCaptureCoordinator combinedCoordinator)
            {
                combinedCoordinator.SystemAudioLevelChanged += OnSystemAudioLevelChanged;
                combinedCoordinator.SystemAudioFaulted += OnSystemAudioFaulted;
                combinedCoordinator.MicrophoneLevelChanged += OnMicrophoneLevelChanged;
                combinedCoordinator.MicrophoneFaulted += OnMicrophoneFaulted;
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
            StatusMessage = eventArgs.ErrorMessage ?? (eventArgs.CurrentState == RecordingSessionState.Completed ? GetCompletedStatusMessage() : "Recording failed");

            if (eventArgs.CurrentState == RecordingSessionState.Completed &&
                _captureCoordinator is AiMeetingAssistant.Windows.Capture.CombinedCaptureCoordinator completedCoordinator)
            {
                _latestSessionDirectory = completedCoordinator.LastCompletedSessionDirectory;
                TranscriptionStatusMessage = _modelPath is null
                    ? "Recording ready, but no local transcription model is installed."
                    : "Recording ready for local transcription.";
                OnPropertyChanged(nameof(CanTranscribeLatest));
            }

            if (_captureCoordinator is AiMeetingAssistant.Windows.Capture.CombinedCaptureCoordinator combinedCoordinator)
            {
                combinedCoordinator.SystemAudioLevelChanged -= OnSystemAudioLevelChanged;
                combinedCoordinator.SystemAudioFaulted -= OnSystemAudioFaulted;
                combinedCoordinator.MicrophoneLevelChanged -= OnMicrophoneLevelChanged;
                combinedCoordinator.MicrophoneFaulted -= OnMicrophoneFaulted;
            }
        }
        else if (eventArgs.CurrentState == RecordingSessionState.Recording)
        {
            StatusMessage = "Screen and audio recording in progress...";
        }

        RaiseCommandStates();
    }

    private void OnRecordingTimerTick(object? sender, EventArgs eventArgs) =>
        OnPropertyChanged(nameof(RecordingElapsedLabel));

    private string GetCompletedStatusMessage()
    {
        if (_captureCoordinator is not AiMeetingAssistant.Windows.Capture.CombinedCaptureCoordinator combined || combined.LastAlignment is null)
            return "Video and audio saved to artifacts/captures/";
        var alignment = combined.LastAlignment;
        if (alignment.EndSpreadMilliseconds is null)
            return $"Saved · alignment {alignment.Status}: {alignment.Detail}";
        return $"Saved · {alignment.Status} · end spread {alignment.EndSpreadMilliseconds:F0} ms";
    }

    private void RaiseCommandStates()
    {
        ToggleRecordingCommand.RaiseCanExecuteChanged();
        RefreshSourcesCommand.RaiseCanExecuteChanged();
        TranscribeLatestCommand.RaiseCanExecuteChanged();
        CancelTranscriptionCommand.RaiseCanExecuteChanged();
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
