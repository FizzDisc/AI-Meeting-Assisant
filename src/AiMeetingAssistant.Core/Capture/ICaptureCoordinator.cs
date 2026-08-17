namespace AiMeetingAssistant.Core.Capture;

public interface ICaptureCoordinator
{
    Task StartAsync(CapturePlan plan, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed record CapturePlan(string ScreenSourceId, string SystemAudioSourceId, string MicrophoneSourceId);
