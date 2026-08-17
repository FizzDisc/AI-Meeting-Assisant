using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Core.Recording;

namespace AiMeetingAssistant.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly CapturePlan SimulationPlan = new("pending-screen", "pending-system", "pending-microphone");
    private readonly RecordingSession _recordingSession = new(new SprintOneSimulationCaptureCoordinator());
    private string? _errorMessage;

    public MainWindowViewModel()
    {
        ToggleRecordingCommand = new AsyncRelayCommand(ToggleRecordingAsync, CanToggleRecording);
        _recordingSession.StateChanged += OnRecordingStateChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand ToggleRecordingCommand { get; }

    public RecordingSessionState State => _recordingSession.State;

    public bool IsRecording => State is RecordingSessionState.Recording;

    public string StateLabel => State switch
    {
        RecordingSessionState.Idle => "Ready",
        RecordingSessionState.Preparing => "Preparing simulation...",
        RecordingSessionState.Recording => "Recording simulation active",
        RecordingSessionState.Stopping => "Stopping simulation...",
        RecordingSessionState.Completed => "Simulation completed",
        RecordingSessionState.Failed => "Simulation failed",
        _ => State.ToString()
    };

    public string RecordingButtonLabel => IsRecording ? "Stop simulation" : "Start simulation";

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public IReadOnlyList<PipelineStep> PipelineSteps { get; } =
    [
        new("Capture screen and audio", "Sprint 1"),
        new("WhisperX transcription", "Sprint 2"),
        new("Speaker diarization", "Sprint 3"),
        new("Minutes and action items", "Sprint 4"),
        new("Knowledge base", "Later")
    ];

    private bool CanToggleRecording() => State is RecordingSessionState.Idle
        or RecordingSessionState.Recording
        or RecordingSessionState.Completed
        or RecordingSessionState.Failed;

    private async Task ToggleRecordingAsync()
    {
        ErrorMessage = null;
        try
        {
            if (IsRecording)
            {
                await _recordingSession.StopAsync();
            }
            else
            {
                await _recordingSession.StartAsync(SimulationPlan);
            }
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
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(RecordingButtonLabel));
        ToggleRecordingCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));
}

public sealed record PipelineStep(string Name, string Phase);
