namespace AiMeetingAssistant.Desktop.ViewModels;

public sealed class MainWindowViewModel
{
    public IReadOnlyList<PipelineStep> PipelineSteps { get; } =
    [
        new("Capture screen and audio", "Sprint 1"),
        new("WhisperX transcription", "Sprint 2"),
        new("Speaker diarization", "Sprint 3"),
        new("Minutes and action items", "Sprint 4"),
        new("Knowledge base", "Later")
    ];
}

public sealed record PipelineStep(string Name, string Phase);

