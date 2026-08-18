namespace AiMeetingAssistant.Core.Capture;

public interface IScreenCaptureProvider : IAsyncDisposable
{
    event EventHandler? CaptureStarted;
    event EventHandler<CaptureErrorEventArgs>? CaptureFaulted;

    bool IsCapturing { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
