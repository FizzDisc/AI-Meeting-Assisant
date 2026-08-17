namespace AiMeetingAssistant.Core.Capture;

public interface ICaptureCoordinator
{
    event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

    Task StartAsync(CapturePlan plan, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed class CaptureErrorEventArgs(string errorMessage, Exception? exception = null) : EventArgs
{
    public string ErrorMessage { get; } = errorMessage;
    public Exception? Exception { get; } = exception;
}

public sealed record CapturePlan(string ScreenSourceId, string SystemAudioSourceId, string MicrophoneSourceId);
