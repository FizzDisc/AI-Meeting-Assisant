namespace AiMeetingAssistant.Core.Capture;

public sealed record CaptureSource(string Id, string DisplayName, CaptureSourceKind Kind, bool IsAvailable);

public enum CaptureSourceKind
{
    Screen,
    SystemAudio,
    Microphone
}

