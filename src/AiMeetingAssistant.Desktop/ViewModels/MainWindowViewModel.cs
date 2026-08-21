using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Core.Meetings;
using AiMeetingAssistant.Core.Recording;
using AiMeetingAssistant.Core.Status;
using AiMeetingAssistant.Core.Transcripts;
using AiMeetingAssistant.Windows.Worker;

namespace AiMeetingAssistant.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ICaptureSourceDiscovery _sourceDiscovery;
    private readonly RecordingSession _recordingSession;
    private readonly ICaptureCoordinator _captureCoordinator;
    private readonly PythonWorkerClient? _workerClient;
    private readonly IncrementalTranscriptionCoordinator? _incrementalTranscription;
    private string? _modelPath;
    private readonly string? _diarizationModelPath;
    private readonly string _captureBaseDirectory;
    private string _computePreference;
    private readonly Stopwatch _recordingStopwatch = new();
    private readonly Stopwatch _transcriptionStopwatch = new();
    private readonly DispatcherTimer _recordingTimer;
    private readonly DispatcherTimer _processingTimer;
    private readonly AudioSignalHealthMonitor _systemAudioHealth = new();
    private readonly AudioSignalHealthMonitor _microphoneHealth = new();
    private string _processingPhase = "Worker active";
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
    private string _audioHealthMessage = "";
    private string? _statusMessage;
    private string? _latestSessionDirectory;
    private CancellationTokenSource? _transcriptionCancellation;
    private bool _isTranscribing;
    private double _transcriptionProgress;
    private string _transcriptionStatusMessage = "Complete a recording to enable local transcription.";
    private string? _transcriptPath;
    private IReadOnlyList<MeetingLibraryEntry> _meetingSessions = [];
    private MeetingLibraryEntry? _selectedMeetingSession;
    private string _meetingLibraryStatus = "No local recordings found.";
    private bool _isScreenCaptureEnabled = AppPreferences.LoadScreenCaptureEnabled();
    private bool _isShuttingDown;
    private TaskCompletionSource? _transcriptionCompletion;
    private readonly OperationalStatusLog _statusLog = new(50);
    private IReadOnlyList<OperationalStatusEntry> _statusLogEntries = [];
    private string _runtimeStatus = "AI runtime not loaded yet.";
    private string _transcriptionActivityDetail = "The worker is idle.";
    private bool _isTranscriptionIndeterminate;
    private IReadOnlyList<SpeechModelOption> _installedSpeechModels = [];
    private SpeechModelOption? _selectedSpeechModel;
    private IReadOnlyList<TranscriptRunInfo> _selectedTranscriptRuns = [];
    private TranscriptRunInfo? _selectedTranscriptRun;
    private readonly Queue<string> _incrementalFinalizationQueue = new();
    private bool _isIncrementalFinalizationQueueRunning;

    public MainWindowViewModel(ICaptureSourceDiscovery sourceDiscovery, ICaptureCoordinator captureCoordinator,
        PythonWorkerClient? workerClient = null, string? modelPath = null, string captureBaseDirectory = "artifacts/captures", string computePreference = "automatic", string? diarizationModelPath = null, string? modelId = null)
    {
        _sourceDiscovery = sourceDiscovery;
        _captureCoordinator = captureCoordinator;
        _workerClient = workerClient;
        if (workerClient is not null)
        {
            _incrementalTranscription = new(workerClient);
            _incrementalTranscription.StatusChanged += OnIncrementalTranscriptionStatusChanged;
        }
        _modelPath = modelPath;
        _diarizationModelPath = diarizationModelPath;
        _captureBaseDirectory = Path.GetFullPath(captureBaseDirectory);
        _computePreference = computePreference;
        _recordingSession = new(captureCoordinator);
        _recordingTimer = new(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _recordingTimer.Tick += OnRecordingTimerTick;
        _processingTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _processingTimer.Tick += OnProcessingTimerTick;
        ToggleRecordingCommand = new AsyncRelayCommand(ToggleRecordingAsync, CanToggleRecording);
        RefreshSourcesCommand = new AsyncRelayCommand(RefreshSourcesAsync, () => CanChangeSources);
        TranscribeLatestCommand = new AsyncRelayCommand(TranscribeLatestAsync, () => CanTranscribeLatest);
        CancelTranscriptionCommand = new AsyncRelayCommand(CancelTranscriptionAsync, () => IsTranscribing);
        RefreshMeetingLibraryCommand = new AsyncRelayCommand(RefreshMeetingLibraryAsync, () => !IsRecording && !IsTranscribing);
        TranscribeSelectedCommand = new AsyncRelayCommand(TranscribeSelectedAsync, () => CanTranscribeSelected);
        ClearStatusLogCommand = new AsyncRelayCommand(ClearStatusLogAsync);
        RefreshInstalledSpeechModels(modelId, modelPath);
        _recordingSession.StateChanged += OnRecordingStateChanged;
        if (captureCoordinator is AiMeetingAssistant.Windows.Capture.CombinedCaptureCoordinator combined)
            combined.IncrementalAudioChunkReady += OnIncrementalAudioChunkReady;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand ToggleRecordingCommand { get; }

    public AsyncRelayCommand RefreshSourcesCommand { get; }
    public AsyncRelayCommand TranscribeLatestCommand { get; }
    public AsyncRelayCommand CancelTranscriptionCommand { get; }
    public AsyncRelayCommand RefreshMeetingLibraryCommand { get; }
    public AsyncRelayCommand TranscribeSelectedCommand { get; }
    public AsyncRelayCommand ClearStatusLogCommand { get; }

    public RecordingSessionState State => _recordingSession.State;

    public bool IsRecording => State is RecordingSessionState.Recording;

    public bool CanChangeSources => !_isDiscoveringSources && State is RecordingSessionState.Idle
        or RecordingSessionState.Completed
        or RecordingSessionState.Failed;

    public IReadOnlyList<SpeechModelOption> InstalledSpeechModels { get => _installedSpeechModels; private set { _installedSpeechModels = value; OnPropertyChanged(); } }
    public SpeechModelOption? SelectedSpeechModel { get => _selectedSpeechModel; set { _selectedSpeechModel = value; _modelPath = value?.ModelPath; OnPropertyChanged(); OnPropertyChanged(nameof(CanTranscribeLatest)); OnPropertyChanged(nameof(CanTranscribeSelected)); TranscribeLatestCommand.RaiseCanExecuteChanged(); TranscribeSelectedCommand.RaiseCanExecuteChanged(); } }

    public bool CanTranscribeLatest => !IsTranscribing && _workerClient is not null &&
        _latestSessionDirectory is not null && Directory.Exists(_latestSessionDirectory) &&
        _modelPath is not null && Directory.Exists(_modelPath);

    public bool IsScreenCaptureEnabled
    {
        get => _isScreenCaptureEnabled;
        set
        {
            _isScreenCaptureEnabled = value;
            AppPreferences.SaveScreenCaptureEnabled(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScreenSelectionEnabled));
            ToggleRecordingCommand.RaiseCanExecuteChanged();
        }
    }

    public bool ScreenSelectionEnabled => CanChangeSources && IsScreenCaptureEnabled;

    public IReadOnlyList<MeetingLibraryEntry> MeetingSessions
    {
        get => _meetingSessions;
        private set { _meetingSessions = value; OnPropertyChanged(); }
    }

    public MeetingLibraryEntry? SelectedMeetingSession
    {
        get => _selectedMeetingSession;
        set
        {
            _selectedMeetingSession = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanOpenSelectedTranscript));
            OnPropertyChanged(nameof(CanTranscribeSelected));
            OnPropertyChanged(nameof(CanDeleteSelectedMeeting));
            TranscribeSelectedCommand.RaiseCanExecuteChanged();
            RefreshSelectedTranscriptRuns();
        }
    }

    public string MeetingLibraryStatus
    {
        get => _meetingLibraryStatus;
        private set { _meetingLibraryStatus = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<TranscriptRunInfo> SelectedTranscriptRuns { get => _selectedTranscriptRuns; private set { _selectedTranscriptRuns = value; OnPropertyChanged(); } }
    public TranscriptRunInfo? SelectedTranscriptRun { get => _selectedTranscriptRun; set { _selectedTranscriptRun = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanOpenSelectedTranscript)); } }

    public bool CanOpenSelectedTranscript => SelectedTranscriptRun is not null;
    public bool CanDeleteSelectedMeeting => SelectedMeetingSession is not null && !IsRecording && !IsTranscribing;
    public bool CanTranscribeSelected => !IsTranscribing && SelectedMeetingSession?.CanTranscribe == true &&
        _workerClient is not null && _modelPath is not null && Directory.Exists(_modelPath);

    public string StateLabel => State switch
    {
        RecordingSessionState.Idle when IsDiscoveringSources => "Discovering Windows devices...",
        RecordingSessionState.Idle when IsTranscribing => "Ready · processing previous recording",
        RecordingSessionState.Idle => "Ready",
        RecordingSessionState.Preparing => "Preparing video and audio streams...",
        RecordingSessionState.Recording => "Recording screen and audio",
        RecordingSessionState.Stopping => "Finalizing video and audio...",
        RecordingSessionState.Completed when IsTranscribing => "Processing previous recording",
        RecordingSessionState.Completed => "Recording completed",
        RecordingSessionState.Failed => "Recording failed",
        _ => State.ToString()
    };

    public string RuntimeStatus
    {
        get => _runtimeStatus;
        private set { _runtimeStatus = value; OnPropertyChanged(); }
    }

    public string OperationalStatusMessage => ErrorMessage ?? StatusMessage ?? StateLabel;

    public IReadOnlyList<OperationalStatusEntry> StatusLogEntries
    {
        get => _statusLogEntries;
        private set { _statusLogEntries = value; OnPropertyChanged(); }
    }

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
            OnPropertyChanged(nameof(ScreenSelectionEnabled));
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
        set
        {
            var previous = _selectedMicrophone;
            _selectedMicrophone = value;
            OnPropertyChanged();
            ToggleRecordingCommand.RaiseCanExecuteChanged();
            if (IsRecording && value is not null && previous?.Id != value.Id) _ = SwitchMicrophoneAsync(value, previous);
        }
    }

    public bool MicrophoneSelectionEnabled => CanChangeSources || IsRecording;

    private async Task SwitchMicrophoneAsync(CaptureSource replacement, CaptureSource? previous)
    {
        if (_captureCoordinator is not AiMeetingAssistant.Windows.Capture.CombinedCaptureCoordinator combined) return;
        try
        {
            StatusMessage = $"Switching microphone to {replacement.DisplayName}...";
            await combined.SwitchMicrophoneAsync(replacement.Id);
            StatusMessage = $"Microphone switched to {replacement.DisplayName}.";
            AddStatus("CAPTURE", StatusMessage);
        }
        catch (Exception exception)
        {
            _selectedMicrophone = previous;
            OnPropertyChanged(nameof(SelectedMicrophone));
            ErrorMessage = $"Microphone handover failed: {FormatExceptionChain(exception)}";
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OperationalStatusMessage));
            if (!string.IsNullOrWhiteSpace(value)) AddStatus("ERROR", value);
        }
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

    public string AudioHealthMessage
    {
        get => _audioHealthMessage;
        private set
        {
            if (_audioHealthMessage == value) return;
            var recovered = _audioHealthMessage.Length > 0 && value.Length == 0;
            _audioHealthMessage = value;
            OnPropertyChanged();
            if (value.Length > 0) AddStatus("WARNING", value);
            else if (recovered) AddStatus("INFO", "Audio signal health recovered.");
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OperationalStatusMessage));
            if (!string.IsNullOrWhiteSpace(value)) AddStatus("INFO", value);
        }
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
            OnPropertyChanged(nameof(CanDeleteSelectedMeeting));
            OnPropertyChanged(nameof(StateLabel));
            RaiseCommandStates();
        }
    }

    public double TranscriptionProgress
    {
        get => _transcriptionProgress;
        private set { _transcriptionProgress = value; OnPropertyChanged(); }
    }

    public string TranscriptionActivityDetail
    {
        get => _transcriptionActivityDetail;
        private set { _transcriptionActivityDetail = value; OnPropertyChanged(); }
    }

    public bool IsTranscriptionIndeterminate
    {
        get => _isTranscriptionIndeterminate;
        private set { _isTranscriptionIndeterminate = value; OnPropertyChanged(); }
    }

    public string TranscriptionStatusMessage
    {
        get => _transcriptionStatusMessage;
        private set
        {
            if (_transcriptionStatusMessage == value) return;
            _transcriptionStatusMessage = value;
            OnPropertyChanged();
            AddStatus("AI", value);
        }
    }

    public string? TranscriptPath
    {
        get => _transcriptPath;
        private set { _transcriptPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTranscript)); }
    }

    public bool HasTranscript => TranscriptPath is not null && File.Exists(TranscriptPath);

    public Dispatcher? UIDispatcher
    {
        get => _uiDispatcher;
        set => _uiDispatcher = value;
    }

    public async Task InitializeAsync()
    {
        AddStatus("STARTUP", "Starting AI Meeting Assistant...");
        RuntimeStatus = "Loading AI runtime...";
        AddStatus("STARTUP", RuntimeStatus);
        CaptureRecoveryReport? recovery = null;
        if (_captureCoordinator is AiMeetingAssistant.Windows.Capture.CombinedCaptureCoordinator combined)
        {
            recovery = combined.RecoverInterruptedSessions();
            TranscriptPath = combined.FindLatestTranscript();
            if (TranscriptPath is not null)
            {
                _latestSessionDirectory = Directory.GetParent(Path.GetDirectoryName(TranscriptPath)!)?.FullName;
                TranscriptionStatusMessage = "Existing local transcript ready to open.";
            }
        }
        await RefreshSourcesAsync();
        await RefreshMeetingLibraryAsync();
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
                RuntimeStatus = health.MlReady
                    ? $"Ready · {health.Diagnostics.Compute.Mode.ToUpperInvariant()}/{health.Diagnostics.Compute.ComputeType} · Python {health.PythonVersion}"
                    : health.RuntimeSupported ? "Worker connected · setup required" : $"Unsupported Python {health.PythonVersion}";
                AddStatus(health.MlReady ? "READY" : "WARNING", RuntimeStatus);
                if (_modelPath is null)
                    TranscriptionStatusMessage = "No local model installed. Use the future Model Manager or development installer.";
            }
            catch (Exception exception)
            {
                RuntimeStatus = "AI runtime unavailable";
                ErrorMessage = $"AI worker unavailable: {exception.Message}";
            }
        }
    }

    public async Task ShutdownAsync()
    {
        _isShuttingDown = true;
        _transcriptionCancellation?.Cancel();
        if (_transcriptionCompletion is not null)
        {
            try { await _transcriptionCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { /* Worker disposal below is the final bounded fallback. */ }
        }
        _recordingTimer.Stop();
        _processingTimer.Stop();
        _recordingStopwatch.Stop();
        try
        {
            await _recordingSession.ShutdownAsync();
        }
        catch
        {
            // Ignore shutdown errors
        }
        if (_captureCoordinator is AiMeetingAssistant.Windows.Capture.CombinedCaptureCoordinator combined)
            combined.IncrementalAudioChunkReady -= OnIncrementalAudioChunkReady;
        if (_incrementalTranscription is not null)
        {
            _incrementalTranscription.StatusChanged -= OnIncrementalTranscriptionStatusChanged;
            try { await _incrementalTranscription.DisposeAsync(); } catch { }
        }
        if (_workerClient is not null)
        {
            try { await _workerClient.DisposeAsync(); } catch { }
        }
    }

    public void ApplyProcessingSettings(string? modelPath, string computePreference, string? modelId = null)
    {
        if (IsTranscribing) throw new InvalidOperationException("Processing settings cannot change during transcription.");
        _modelPath = modelPath is null ? null : Path.GetFullPath(modelPath);
        _computePreference = computePreference;
        RefreshInstalledSpeechModels(modelId, modelPath);
        TranscriptionStatusMessage = _modelPath is null
            ? "The selected speech model is not installed."
            : $"Processing settings updated · {Path.GetFileName(_modelPath)} · {computePreference}";
        OnPropertyChanged(nameof(CanTranscribeLatest));
        OnPropertyChanged(nameof(CanTranscribeSelected));
        TranscribeLatestCommand.RaiseCanExecuteChanged();
        TranscribeSelectedCommand.RaiseCanExecuteChanged();
        AddStatus("AI", TranscriptionStatusMessage);
    }

    private void RefreshInstalledSpeechModels(string? selectedId, string? customPath)
    {
        var models = LocalModelResolver.SpeechModels
            .Select(model => new SpeechModelOption(model.Id, model.DisplayName, LocalModelResolver.ResolveSpeechModel(model.Id)))
            .Where(model => model.ModelPath is not null && Directory.Exists(model.ModelPath)).ToList();
        if (!string.IsNullOrWhiteSpace(customPath) && models.All(model => !string.Equals(model.ModelPath, customPath, StringComparison.OrdinalIgnoreCase)))
            models.Add(new("custom", $"Custom · {Path.GetFileName(customPath)}", Path.GetFullPath(customPath)));
        InstalledSpeechModels = models;
        SelectedSpeechModel = models.FirstOrDefault(model => model.Id == selectedId)
            ?? models.FirstOrDefault(model => string.Equals(model.ModelPath, customPath, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault();
    }

    private void RefreshSelectedTranscriptRuns()
    {
        SelectedTranscriptRuns = SelectedMeetingSession is null ? [] : TranscriptRunCatalog.Discover(SelectedMeetingSession.SessionDirectory);
        SelectedTranscriptRun = SelectedTranscriptRuns.FirstOrDefault();
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
        (CanChangeSources && (!IsScreenCaptureEnabled || SelectedScreen is not null) && SelectedSystemAudio is not null && SelectedMicrophone is not null);

    private Task RefreshMeetingLibraryAsync()
    {
        var result = MeetingLibrary.Discover(_captureBaseDirectory);
        MeetingSessions = result.Sessions;
        SelectedMeetingSession = MeetingSessions.FirstOrDefault(session => session.SessionDirectory == SelectedMeetingSession?.SessionDirectory)
            ?? MeetingSessions.FirstOrDefault();
        MeetingLibraryStatus = result.Issues.Count == 0
            ? $"{MeetingSessions.Count} local recording(s)"
            : $"{MeetingSessions.Count} recording(s) · {result.Issues.Count} issue(s)";
        return Task.CompletedTask;
    }

    private async Task TranscribeSelectedAsync()
    {
        if (!CanTranscribeSelected || SelectedMeetingSession is null) return;
        _latestSessionDirectory = SelectedMeetingSession.SessionDirectory;
        TranscriptPath = SelectedMeetingSession.TranscriptPath;
        await TranscribeLatestAsync();
        await RefreshMeetingLibraryAsync();
        SelectedMeetingSession = MeetingSessions.FirstOrDefault(session => session.SessionDirectory == _latestSessionDirectory);
    }

    public async Task DeleteSelectedMeetingAsync()
    {
        if (!CanDeleteSelectedMeeting || SelectedMeetingSession is null) return;
        var deletedDirectory = SelectedMeetingSession.SessionDirectory;
        MeetingLibrary.DeleteSession(_captureBaseDirectory, deletedDirectory);
        if (_latestSessionDirectory is not null &&
            string.Equals(Path.GetFullPath(_latestSessionDirectory), Path.GetFullPath(deletedDirectory), StringComparison.OrdinalIgnoreCase))
        {
            _latestSessionDirectory = null;
            TranscriptPath = null;
            TranscriptionStatusMessage = "Select a recording from the library to transcribe it.";
        }
        await RefreshMeetingLibraryAsync();
        MeetingLibraryStatus = $"Recording deleted · {MeetingSessions.Count} local recording(s) remaining";
        ErrorMessage = null;
        StatusMessage = "Recording deleted from the local library.";
    }

    private async Task TranscribeLatestAsync()
    {
        if (!CanTranscribeLatest || _workerClient is null || _latestSessionDirectory is null || _modelPath is null)
            return;

        ErrorMessage = null;
        TranscriptPath = null;
        TranscriptionProgress = 0;
        _transcriptionStopwatch.Restart();
        _processingPhase = "Starting worker job";
        _processingTimer.Start();
        TranscriptionActivityDetail = "Starting worker job · elapsed 00:00";
        IsTranscriptionIndeterminate = true;
        IsTranscribing = true;
        _transcriptionCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _transcriptionCancellation = new CancellationTokenSource();
        var token = _transcriptionCancellation.Token;
        string? activeJobId = null;
        var modelLoadNoticeLogged = false;
        try
        {
            var microphone = Directory.GetFiles(_latestSessionDirectory, "microphone_*.wav").SingleOrDefault();
            var systemAudio = Directory.GetFiles(_latestSessionDirectory, "system_audio_*.wav").SingleOrDefault();
            if (microphone is null || systemAudio is null)
                throw new InvalidDataException("The latest session does not contain exactly one microphone and system-audio WAV file.");

            var selectedModel = SelectedSpeechModel ?? throw new InvalidOperationException("Select an installed speech model.");
            var outputPath = Path.Combine(_latestSessionDirectory, "processing", $"transcript_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{selectedModel.Id}.json");
            TranscriptionStatusMessage = "Queuing local transcription...";
            var job = await _workerClient.StartTranscriptionAsync([microphone, systemAudio], selectedModel.ModelPath!, outputPath,
                computePreference: _computePreference, diarizationModelPath: _diarizationModelPath, modelId: selectedModel.Id,
                openVinoModelPath: LocalModelResolver.ResolveOpenVinoSpeechModel(selectedModel.Id),
                openVinoRuntimePath: LocalModelResolver.ResolveOpenVinoRuntime(),
                sileroVadPath: LocalModelResolver.ResolveSileroVad(),
                torchXpuRuntimePath: LocalModelResolver.ResolveTorchXpuRuntime(), cancellationToken: token);
            activeJobId = job.JobId;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                job = await _workerClient.GetTranscriptionStatusAsync(job.JobId, token);
                TranscriptionProgress = Math.Clamp(job.Progress * 100, 0, 100);
                TranscriptionStatusMessage = FormatTranscriptionStatus(job);
                IsTranscriptionIndeterminate = job.Status is "queued" or "normalizing" or "loading-model" or "loading-openvino-model" or "detecting-speech" or "loading-diarization-model" or "reusing-transcription" or "reusing-speakers";
                TranscriptionActivityDetail = FormatTranscriptionActivity(job.Status, _transcriptionStopwatch.Elapsed);
                if (!modelLoadNoticeLogged && job.Status == "loading-model" && _transcriptionStopwatch.Elapsed >= TimeSpan.FromSeconds(15))
                {
                    AddStatus("AI", "Speech model is still loading on CPU; the worker remains active.");
                    modelLoadNoticeLogged = true;
                }
                if (job.Status == "completed")
                {
                    TranscriptPath = job.OutputPath ?? outputPath;
                    CopyAtomic(TranscriptPath, Path.Combine(_latestSessionDirectory, "processing", "transcript.json"));
                    break;
                }
                if (job.Status == "failed") throw new InvalidOperationException(job.Error ?? "Local transcription failed.");
                if (job.Status == "cancelled") break;
                await Task.Delay(500, token);
            }
        }
        catch (OperationCanceledException)
        {
            TranscriptionStatusMessage = _isShuttingDown ? "Stopping local transcription for shutdown..." : "Cancelling local transcription...";
            if (!_isShuttingDown && activeJobId is not null)
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
            if (!_isShuttingDown)
            {
                ErrorMessage = $"Transcription failed: {exception.Message}";
                TranscriptionStatusMessage = "Transcription failed.";
            }
        }
        finally
        {
            _transcriptionStopwatch.Stop();
            _processingTimer.Stop();
            IsTranscriptionIndeterminate = false;
            TranscriptionActivityDetail = $"Worker idle · last job {_transcriptionStopwatch.Elapsed:mm\\:ss}";
            _transcriptionCancellation?.Dispose();
            _transcriptionCancellation = null;
            IsTranscribing = false;
            _transcriptionCompletion?.TrySetResult();
            _transcriptionCompletion = null;
        }
    }

    private Task CancelTranscriptionAsync()
    {
        _transcriptionCancellation?.Cancel();
        return Task.CompletedTask;
    }

    private static void CopyAtomic(string source, string destination)
    {
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try { File.Copy(source, temporary, true); File.Move(temporary, destination, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string FormatTranscriptionStatus(TranscriptionJobStatus job) => job.Status switch
    {
        "queued" => "Transcription queued...",
        "normalizing" => "Mixing and normalizing audio...",
        "loading-model" => "Loading local speech model...",
        "loading-openvino-model" => "Loading Whisper on Intel GPU...",
        "checking-audio" => "Checking finalized audio signal...",
        "detecting-speech" => $"Detecting speech in {FormatSource(job.Source)}...",
        "reusing-transcription" => "Reusing cached transcription...",
        "transcribing" => $"Transcribing {FormatSource(job.Source)} on {job.Device?.ToUpperInvariant() ?? "local hardware"}...",
        "loading-diarization-model" => "Loading local speaker model...",
        "diarizing" => $"Detecting speakers in system audio on {job.Device?.ToUpperInvariant() ?? "CPU"}...",
        "reusing-speakers" => "Reusing cached speaker analysis...",
        "assigning-speakers" => "Assigning speakers to transcript segments...",
        "completed" when job.SkippedSources?.Count > 0 =>
            $"Transcription completed · skipped silent {string.Join(" and ", job.SkippedSources.Select(FormatSource))} · {job.SegmentCount ?? 0} segment(s)",
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

    private static string FormatTranscriptionActivity(string status, TimeSpan elapsed)
    {
        var phase = status switch
        {
            "loading-model" => "Worker active · loading the local model on CPU; a cold start can take several minutes",
            "loading-openvino-model" => "Worker active · compiling Whisper for the Intel GPU",
            "detecting-speech" => "Worker active · detecting speech before Intel GPU transcription",
            "reusing-transcription" => "Worker active · reusing cached transcription",
            "loading-diarization-model" => "Worker active · loading the local speaker model",
            "normalizing" => "Worker active · preparing audio",
            "transcribing" => "Worker active · decoding speech",
            "diarizing" => "Worker active · detecting speaker turns",
            "reusing-speakers" => "Worker active · reusing cached speaker turns",
            "assigning-speakers" => "Worker active · reconciling transcript timestamps",
            "queued" => "Worker active · job queued",
            _ => $"Worker active · {status}"
        };
        return $"{phase} · elapsed {elapsed:mm\\:ss}";
    }

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

            if ((IsScreenCaptureEnabled && SelectedScreen is null) || SelectedSystemAudio is null || SelectedMicrophone is null)
                throw new InvalidOperationException("Select the required capture sources first.");

            if (_captureCoordinator is AiMeetingAssistant.Windows.Capture.CombinedCaptureCoordinator combinedCoordinator)
            {
                combinedCoordinator.SystemAudioLevelChanged += OnSystemAudioLevelChanged;
                combinedCoordinator.SystemAudioFaulted += OnSystemAudioFaulted;
                combinedCoordinator.MicrophoneLevelChanged += OnMicrophoneLevelChanged;
                combinedCoordinator.MicrophoneFaulted += OnMicrophoneFaulted;
            }

            var plan = new CapturePlan(IsScreenCaptureEnabled ? SelectedScreen!.Id : string.Empty, SelectedSystemAudio.Id, SelectedMicrophone.Id);
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
        _systemAudioHealth.Observe(eventArgs.Level.RmsDb, DateTimeOffset.UtcNow);
        SystemAudioLevel = NormalizeLevel(eventArgs.Level.RmsDb);
    }

    private void OnSystemAudioFaulted(object? sender, AudioCaptureFaultEventArgs eventArgs)
    {
        ErrorMessage = $"System audio error: {eventArgs.ErrorMessage}";
    }

    private void OnMicrophoneLevelChanged(object? sender, AudioFrameCapturedEventArgs eventArgs)
    {
        _microphoneHealth.Observe(eventArgs.Level.RmsDb, DateTimeOffset.UtcNow);
        MicrophoneLevel = NormalizeLevel(eventArgs.Level.RmsDb);
    }

    private void OnMicrophoneFaulted(object? sender, AudioCaptureFaultEventArgs eventArgs)
    {
        ErrorMessage = $"Microphone error: {eventArgs.ErrorMessage}";
    }

    private void OnIncrementalAudioChunkReady(object? sender, IncrementalAudioChunkReadyEventArgs eventArgs)
    {
        var selected = SelectedSpeechModel;
        if (_incrementalTranscription is null || selected?.ModelPath is null || !Directory.Exists(selected.ModelPath)) return;
        var options = new IncrementalTranscriptionOptions(selected.ModelPath, selected.Id, _computePreference,
            LocalModelResolver.ResolveOpenVinoSpeechModel(selected.Id), LocalModelResolver.ResolveOpenVinoRuntime(),
            LocalModelResolver.ResolveSileroVad(), LocalModelResolver.ResolveTorchXpuRuntime(), _diarizationModelPath);
        _incrementalTranscription.TryQueue(eventArgs, options);
    }

    private async Task FinalizeIncrementalSessionAsync(string sessionDirectory)
    {
        var selected = SelectedSpeechModel;
        if (_incrementalTranscription is null || selected?.ModelPath is null) return;
        IsTranscribing = true;
        _transcriptionStopwatch.Restart();
        _processingPhase = "Finalizing incremental transcript";
        _processingTimer.Start();
        IsTranscriptionIndeterminate = true;
        TranscriptionProgress = 0;
        TranscriptionStatusMessage = "Finalizing incremental transcript...";
        _transcriptionCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _transcriptionCancellation = new CancellationTokenSource();
        try
        {
            var options = new IncrementalTranscriptionOptions(selected.ModelPath, selected.Id, _computePreference,
                LocalModelResolver.ResolveOpenVinoSpeechModel(selected.Id), LocalModelResolver.ResolveOpenVinoRuntime(),
                LocalModelResolver.ResolveSileroVad(), LocalModelResolver.ResolveTorchXpuRuntime(), _diarizationModelPath);
            TranscriptPath = await _incrementalTranscription.FinalizeSessionAsync(sessionDirectory, options,
                _transcriptionCancellation.Token);
            TranscriptionProgress = 100;
            TranscriptionStatusMessage = "Incremental transcript and speaker analysis completed.";
            await RefreshMeetingLibraryAsync();
        }
        catch (OperationCanceledException)
        {
            TranscriptionStatusMessage = "Incremental transcript finalization cancelled; durable chunks were preserved.";
        }
        catch (Exception exception)
        {
            TranscriptionStatusMessage = "Incremental transcript finalization failed; recording and chunks were preserved.";
            AddStatus("WARNING", $"Incremental finalization failed: {exception.Message}");
        }
        finally
        {
            _transcriptionCancellation?.Dispose();
            _transcriptionStopwatch.Stop();
            _processingTimer.Stop();
            _transcriptionCancellation = null;
            IsTranscriptionIndeterminate = false;
            IsTranscribing = false;
            _transcriptionCompletion?.TrySetResult();
            _transcriptionCompletion = null;
        }
    }

    private void QueueIncrementalFinalization(string sessionDirectory)
    {
        _incrementalFinalizationQueue.Enqueue(sessionDirectory);
        if (_isIncrementalFinalizationQueueRunning)
        {
            AddStatus("AI", "Recording queued for automatic processing after the current meeting.");
            return;
        }
        _isIncrementalFinalizationQueueRunning = true;
        _ = ProcessIncrementalFinalizationQueueAsync();
    }

    private async Task ProcessIncrementalFinalizationQueueAsync()
    {
        try
        {
            while (_incrementalFinalizationQueue.TryDequeue(out var sessionDirectory))
                await FinalizeIncrementalSessionAsync(sessionDirectory);
        }
        finally
        {
            _isIncrementalFinalizationQueueRunning = false;
            if (_incrementalFinalizationQueue.Count > 0)
            {
                _isIncrementalFinalizationQueueRunning = true;
                _ = ProcessIncrementalFinalizationQueueAsync();
            }
        }
    }

    private void OnIncrementalTranscriptionStatusChanged(object? sender, IncrementalTranscriptionStatusEventArgs eventArgs)
    {
        void Update()
        {
            AddStatus(eventArgs.Level, eventArgs.Message);
            _processingPhase = eventArgs.Message;
            TranscriptionActivityDetail = $"{_processingPhase} · elapsed {_transcriptionStopwatch.Elapsed.ToString(@"hh\:mm\:ss")}";
            if (eventArgs.OutputPath is string path && File.Exists(path))
            {
                TranscriptPath = path;
                if (eventArgs.Message.StartsWith("Preliminary transcript", StringComparison.Ordinal))
                    TranscriptionStatusMessage = "Transcript ready; identifying speakers in background...";
                _ = RefreshMeetingLibraryAsync();
            }
        }
        var dispatcher = _uiDispatcher ?? Dispatcher.CurrentDispatcher;
        if (dispatcher.CheckAccess()) Update(); else dispatcher.BeginInvoke(Update);
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
        OnPropertyChanged(nameof(MicrophoneSelectionEnabled));
        OnPropertyChanged(nameof(CanChangeSources));
        OnPropertyChanged(nameof(CanDeleteSelectedMeeting));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(OperationalStatusMessage));
        OnPropertyChanged(nameof(RecordingButtonLabel));

        if (eventArgs.CurrentState == RecordingSessionState.Preparing)
        {
            _recordingTimer.Stop();
            _recordingStopwatch.Reset();
            OnPropertyChanged(nameof(RecordingElapsedLabel));
        }
        else if (eventArgs.CurrentState == RecordingSessionState.Recording)
        {
            var now = DateTimeOffset.UtcNow;
            _systemAudioHealth.Start(now);
            _microphoneHealth.Start(now);
            AudioHealthMessage = "";
            _recordingStopwatch.Start();
            _recordingTimer.Start();
            OnPropertyChanged(nameof(RecordingElapsedLabel));
        }

        // Reset level when recording stops
        if (eventArgs.CurrentState is RecordingSessionState.Completed or RecordingSessionState.Failed)
        {
            _systemAudioHealth.Stop();
            _microphoneHealth.Stop();
            AudioHealthMessage = "";
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
                _ = RefreshMeetingLibraryAsync();
                if (_modelPath is not null && completedCoordinator.LastCompletedSessionDirectory is string sessionDirectory)
                    QueueIncrementalFinalization(sessionDirectory);
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
            StatusMessage = IsScreenCaptureEnabled ? "Screen and audio recording in progress..." : "Audio-only recording in progress...";
            AddStatus("AI", _incrementalTranscription is not null && SelectedSpeechModel?.ModelPath is not null
                ? "Live transcription armed; finalized audio chunks will be processed in the background."
                : "Live transcription unavailable because no local speech model is selected.");
        }

        RaiseCommandStates();
    }

    private void OnRecordingTimerTick(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(RecordingElapsedLabel));
        var now = DateTimeOffset.UtcNow;
        var warnings = new List<string>();
        AddAudioHealthWarning(warnings, "System audio", _systemAudioHealth.Evaluate(now), "Verify the selected Teams/output device.");
        AddAudioHealthWarning(warnings, "Microphone", _microphoneHealth.Evaluate(now), "Verify the selected input or hardware mute.");
        AudioHealthMessage = string.Join("  ", warnings);
    }

    private static void AddAudioHealthWarning(List<string> warnings, string source, AudioSignalHealthState state, string guidance)
    {
        if (state == AudioSignalHealthState.NeverDetected) warnings.Add($"No {source.ToLowerInvariant()} signal detected yet. {guidance}");
        else if (state == AudioSignalHealthState.CurrentlySilent) warnings.Add($"{source} has been silent for 45 seconds. {guidance}");
    }

    private void OnProcessingTimerTick(object? sender, EventArgs eventArgs) =>
        TranscriptionActivityDetail = $"{_processingPhase} · elapsed {_transcriptionStopwatch.Elapsed.ToString(@"hh\:mm\:ss")}";

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
        RefreshMeetingLibraryCommand.RaiseCanExecuteChanged();
        TranscribeSelectedCommand.RaiseCanExecuteChanged();
    }

    private Task ClearStatusLogAsync()
    {
        StatusLogEntries = _statusLog.Clear();
        return Task.CompletedTask;
    }

    private void AddStatus(string level, string message)
    {
        StatusLogEntries = _statusLog.Add(level, message);
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

public sealed record SpeechModelOption(string Id, string DisplayName, string? ModelPath);
