namespace AiMeetingAssistant.Core.Capture;

/// <summary>
/// Raised when audio capture starts successfully.
/// </summary>
public sealed class AudioCaptureStartedEventArgs(int sampleRate, int channelCount, int bitDepth) : EventArgs
{
    /// <summary>
    /// Audio sample rate in Hz (e.g., 48000).
    /// </summary>
    public int SampleRate { get; } = sampleRate;

    /// <summary>
    /// Number of audio channels (typically 1 or 2).
    /// </summary>
    public int ChannelCount { get; } = channelCount;

    /// <summary>
    /// Bits per sample (typically 16 or 24).
    /// </summary>
    public int BitDepth { get; } = bitDepth;
}

/// <summary>
/// Raised on each audio frame during recording.
/// </summary>
public sealed class AudioFrameCapturedEventArgs(AudioLevel level, long framePosition, int frameCount, byte[]? pcm16Data = null) : EventArgs
{
    /// <summary>
    /// Current audio level (RMS, peak, frame count).
    /// </summary>
    public AudioLevel Level { get; } = level;

    /// <summary>
    /// Absolute position of this frame in the stream (in frames).
    /// </summary>
    public long FramePosition { get; } = framePosition;

    /// <summary>
    /// Number of frames in this buffer.
    /// </summary>
    public int FrameCount { get; } = frameCount;

    /// <summary>
    /// PCM16 payload for optional background processing. Consumers must treat the buffer as immutable.
    /// Providers that only report levels may leave this value null.
    /// </summary>
    public byte[]? Pcm16Data { get; } = pcm16Data;
}

/// <summary>
/// Raised if audio capture fails or encounters an unrecoverable error.
/// </summary>
public sealed class AudioCaptureFaultEventArgs(string errorMessage, Exception? innerException = null) : EventArgs
{
    /// <summary>
    /// User-friendly error description.
    /// </summary>
    public string ErrorMessage { get; } = errorMessage;

    /// <summary>
    /// The underlying exception, if any.
    /// </summary>
    public Exception? InnerException { get; } = innerException;
}
