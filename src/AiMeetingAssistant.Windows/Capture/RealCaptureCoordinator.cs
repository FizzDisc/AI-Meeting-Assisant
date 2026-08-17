using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Windows.Capture;

/// <summary>
/// Sprint 1.4.1 coordinator: captures one selected render endpoint via WASAPI loopback.
/// </summary>
public sealed class RealCaptureCoordinator : ICaptureCoordinator
{
    private readonly string _captureBaseDirectory;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private IAudioCaptureProvider? _systemAudioCapture;
    private bool _isCapturing;

    public event EventHandler<AudioFrameCapturedEventArgs>? SystemAudioLevelChanged;
    public event EventHandler<AudioCaptureFaultEventArgs>? SystemAudioFaulted;
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
            if (_isCapturing || _systemAudioCapture is not null)
                throw new InvalidOperationException("Capture session is already active.");

            if (string.IsNullOrEmpty(plan.SystemAudioSourceId))
                throw new ArgumentException("System audio source ID is required for Sprint 1.4.1");

            // Ensure capture directory exists
            if (!Directory.Exists(_captureBaseDirectory))
                Directory.CreateDirectory(_captureBaseDirectory);

            var outputPath = CaptureFileNaming.CreateUniqueWavPath(_captureBaseDirectory, "system_audio", DateTime.Now);

            _systemAudioCapture = new WasapiAudioCaptureProvider(plan.SystemAudioSourceId, outputPath, WasapiCaptureMode.Loopback);
            _systemAudioCapture.FrameCaptured += OnSystemAudioFrameCaptured;
            _systemAudioCapture.CaptureFaulted += OnSystemAudioCaptureFaulted;

            await _systemAudioCapture.StartAsync(cancellationToken).ConfigureAwait(false);

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

    private void OnSystemAudioFrameCaptured(object? sender, AudioFrameCapturedEventArgs e)
    {
        SystemAudioLevelChanged?.Invoke(this, e);
    }

    private void OnSystemAudioCaptureFaulted(object? sender, AudioCaptureFaultEventArgs e)
    {
        SystemAudioFaulted?.Invoke(this, e);
        CaptureFailed?.Invoke(this, new(e.ErrorMessage, e.InnerException));
    }

    private async Task CleanupCoreAsync(bool stopFirst, CancellationToken cancellationToken)
    {
        var capture = _systemAudioCapture;
        _systemAudioCapture = null;
        _isCapturing = false;
        if (capture != null)
        {
            try
            {
                capture.FrameCaptured -= OnSystemAudioFrameCaptured;
                capture.CaptureFaulted -= OnSystemAudioCaptureFaulted;
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
