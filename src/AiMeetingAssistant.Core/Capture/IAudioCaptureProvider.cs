namespace AiMeetingAssistant.Core.Capture;

/// <summary>
/// Abstraction for capturing audio from a single endpoint and reporting level changes.
/// Implementations must be thread-safe and handle device loss gracefully.
/// </summary>
public interface IAudioCaptureProvider : IAsyncDisposable
{
    /// <summary>
    /// Raised when audio capture starts and the format is negotiated.
    /// </summary>
    event EventHandler<AudioCaptureStartedEventArgs>? CaptureStarted;

    /// <summary>
    /// Raised on each audio buffer captured.
    /// </summary>
    event EventHandler<AudioFrameCapturedEventArgs>? FrameCaptured;

    /// <summary>
    /// Raised if capture fails or device is lost.
    /// </summary>
    event EventHandler<AudioCaptureFaultEventArgs>? CaptureFaulted;

    /// <summary>
    /// Start capturing audio from the configured endpoint.
    /// Must raise CaptureStarted if successful.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop capturing audio and flush any pending frames.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// True if currently capturing.
    /// </summary>
    bool IsCapturing { get; }
}

/// <summary>
/// Optional capability for replacing captured samples with silence without
/// stopping the endpoint or changing the recording timeline.
/// </summary>
public interface IAudioCaptureSuppression
{
    bool IsAudioSuppressed { get; }
    void SetAudioSuppressed(bool suppressed);
}
