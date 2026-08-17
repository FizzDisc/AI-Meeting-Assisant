using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Windows.Capture;

/// <summary>
/// Real capture coordinator that manages microphone and (later) system audio and screen capture.
/// For Sprint 1.3, only microphone is implemented.
/// </summary>
public sealed class RealCaptureCoordinator : ICaptureCoordinator
{
    private readonly string _captureBaseDirectory;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private IAudioCaptureProvider? _microphoneCapture;
    private bool _isCapturing;

    public event EventHandler<AudioFrameCapturedEventArgs>? MicrophoneLevelChanged;
    public event EventHandler<AudioCaptureFaultEventArgs>? MicrophoneFaulted;
    public event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

    public RealCaptureCoordinator(string captureBaseDirectory = "artifacts/captures")
    {
        _captureBaseDirectory = captureBaseDirectory ?? throw new ArgumentNullException(nameof(captureBaseDirectory));
    }

    public async Task StartAsync(CapturePlan plan, CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isCapturing || _microphoneCapture is not null)
                throw new InvalidOperationException("Capture session is already active.");

        if (string.IsNullOrEmpty(plan.MicrophoneSourceId))
            throw new ArgumentException("Microphone source ID is required for Sprint 1.3");

            // Ensure capture directory exists
            if (!Directory.Exists(_captureBaseDirectory))
                Directory.CreateDirectory(_captureBaseDirectory);

            // Generate output filename with timestamp
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var outputPath = Path.Combine(_captureBaseDirectory, $"microphone_{timestamp}.wav");

            // Ensure unique filename
            int counter = 0;
            while (File.Exists(outputPath) && counter < 100)
            {
                counter++;
                outputPath = Path.Combine(_captureBaseDirectory, $"microphone_{timestamp}_{counter:D2}.wav");
            }

            if (File.Exists(outputPath))
                throw new InvalidOperationException("Unable to generate unique microphone output filename");

            // Start microphone capture
            _microphoneCapture = new WasapiAudioCaptureProvider(plan.MicrophoneSourceId, outputPath);
            _microphoneCapture.FrameCaptured += OnMicrophoneFrameCaptured;
            _microphoneCapture.CaptureFaulted += OnMicrophoneCaptureFaulted;

            await _microphoneCapture.StartAsync(cancellationToken).ConfigureAwait(false);

            _isCapturing = true;
        }
        catch
        {
            await CleanupCoreAsync(stopFirst: false, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CleanupCoreAsync(stopFirst: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private void OnMicrophoneFrameCaptured(object? sender, AudioFrameCapturedEventArgs e)
    {
        MicrophoneLevelChanged?.Invoke(this, e);
    }

    private void OnMicrophoneCaptureFaulted(object? sender, AudioCaptureFaultEventArgs e)
    {
        // Forward to both internal listeners and public CaptureFailed event
        MicrophoneFaulted?.Invoke(this, e);
        CaptureFailed?.Invoke(this, new(e.ErrorMessage, e.InnerException));
    }

    private async Task CleanupCoreAsync(bool stopFirst, CancellationToken cancellationToken)
    {
        var capture = _microphoneCapture;
        _microphoneCapture = null;
        _isCapturing = false;
        if (capture != null)
        {
            try
            {
                capture.FrameCaptured -= OnMicrophoneFrameCaptured;
                capture.CaptureFaulted -= OnMicrophoneCaptureFaulted;
                if (stopFirst && capture.IsCapturing)
                    await capture.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await capture.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
