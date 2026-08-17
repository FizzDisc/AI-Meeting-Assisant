namespace AiMeetingAssistant.Core.Recording;

public sealed class RecordingStateChangedEventArgs(
    RecordingSessionState previousState,
    RecordingSessionState currentState,
    string? errorMessage = null) : EventArgs
{
    public RecordingSessionState PreviousState { get; } = previousState;
    public RecordingSessionState CurrentState { get; } = currentState;
    public string? ErrorMessage { get; } = errorMessage;
}

