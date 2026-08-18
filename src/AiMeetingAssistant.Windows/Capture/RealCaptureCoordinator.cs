using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Windows.Capture;

public sealed class DualAudioCaptureCoordinator : ICaptureCoordinator
{
    private readonly string _captureBaseDirectory;
    private readonly Func<string, string, WasapiCaptureMode, IAudioCaptureProvider> _providerFactory;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private IAudioCaptureProvider? _systemAudioCapture;
    private IAudioCaptureProvider? _microphoneCapture;
    private bool _isCapturing;

    public event EventHandler<AudioFrameCapturedEventArgs>? SystemAudioLevelChanged;
    public event EventHandler<AudioCaptureFaultEventArgs>? SystemAudioFaulted;
    public event EventHandler<AudioFrameCapturedEventArgs>? MicrophoneLevelChanged;
    public event EventHandler<AudioCaptureFaultEventArgs>? MicrophoneFaulted;
    public event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

    public DualAudioCaptureCoordinator(
        string captureBaseDirectory = "artifacts/captures",
        Func<string, string, WasapiCaptureMode, IAudioCaptureProvider>? providerFactory = null)
    {
        _captureBaseDirectory = captureBaseDirectory ?? throw new ArgumentNullException(nameof(captureBaseDirectory));
        _providerFactory = providerFactory ?? ((id, path, mode) => new WasapiAudioCaptureProvider(id, path, mode));
    }

    public async Task StartAsync(CapturePlan plan, CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isCapturing || _systemAudioCapture is not null || _microphoneCapture is not null)
                throw new InvalidOperationException("Capture session is already active.");
            if (string.IsNullOrWhiteSpace(plan.SystemAudioSourceId) || string.IsNullOrWhiteSpace(plan.MicrophoneSourceId))
                throw new ArgumentException("System audio and microphone source IDs are required.");

            Directory.CreateDirectory(_captureBaseDirectory);
            var timestamp = DateTime.Now;
            var systemPath = CaptureFileNaming.CreateUniqueWavPath(_captureBaseDirectory, "system_audio", timestamp);
            var microphonePath = CaptureFileNaming.CreateUniqueWavPath(_captureBaseDirectory, "microphone", timestamp);

            _systemAudioCapture = _providerFactory(plan.SystemAudioSourceId, systemPath, WasapiCaptureMode.Loopback);
            SubscribeSystemAudio(_systemAudioCapture);
            await _systemAudioCapture.StartAsync(cancellationToken).ConfigureAwait(false);

            _microphoneCapture = _providerFactory(plan.MicrophoneSourceId, microphonePath, WasapiCaptureMode.Input);
            SubscribeMicrophone(_microphoneCapture);
            await _microphoneCapture.StartAsync(cancellationToken).ConfigureAwait(false);

            _isCapturing = true;
        }
        catch
        {
            await CleanupCoreAsync(stopFirst: true, CancellationToken.None).ConfigureAwait(false);
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

    private void SubscribeSystemAudio(IAudioCaptureProvider capture)
    {
        capture.FrameCaptured += OnSystemAudioFrameCaptured;
        capture.CaptureFaulted += OnSystemAudioCaptureFaulted;
    }

    private void SubscribeMicrophone(IAudioCaptureProvider capture)
    {
        capture.FrameCaptured += OnMicrophoneFrameCaptured;
        capture.CaptureFaulted += OnMicrophoneCaptureFaulted;
    }

    private void OnSystemAudioFrameCaptured(object? sender, AudioFrameCapturedEventArgs e) => SystemAudioLevelChanged?.Invoke(this, e);
    private void OnMicrophoneFrameCaptured(object? sender, AudioFrameCapturedEventArgs e) => MicrophoneLevelChanged?.Invoke(this, e);

    private void OnSystemAudioCaptureFaulted(object? sender, AudioCaptureFaultEventArgs e)
    {
        SystemAudioFaulted?.Invoke(this, e);
        CaptureFailed?.Invoke(this, new($"System audio: {e.ErrorMessage}", e.InnerException));
    }

    private void OnMicrophoneCaptureFaulted(object? sender, AudioCaptureFaultEventArgs e)
    {
        MicrophoneFaulted?.Invoke(this, e);
        CaptureFailed?.Invoke(this, new($"Microphone: {e.ErrorMessage}", e.InnerException));
    }

    private async Task CleanupCoreAsync(bool stopFirst, CancellationToken cancellationToken)
    {
        var microphone = _microphoneCapture;
        var systemAudio = _systemAudioCapture;
        _microphoneCapture = null;
        _systemAudioCapture = null;
        _isCapturing = false;

        Exception? firstError = null;
        try { await CleanupMicrophoneAsync(microphone, stopFirst, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) { firstError = ex; }
        try { await CleanupSystemAudioAsync(systemAudio, stopFirst, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) { firstError ??= ex; }
        if (firstError is not null) throw firstError;
    }

    private async Task CleanupMicrophoneAsync(IAudioCaptureProvider? capture, bool stopFirst, CancellationToken token)
    {
        if (capture is null) return;
        capture.FrameCaptured -= OnMicrophoneFrameCaptured;
        capture.CaptureFaulted -= OnMicrophoneCaptureFaulted;
        try { if (stopFirst && capture.IsCapturing) await capture.StopAsync(token).ConfigureAwait(false); }
        finally { await capture.DisposeAsync().ConfigureAwait(false); }
    }

    private async Task CleanupSystemAudioAsync(IAudioCaptureProvider? capture, bool stopFirst, CancellationToken token)
    {
        if (capture is null) return;
        capture.FrameCaptured -= OnSystemAudioFrameCaptured;
        capture.CaptureFaulted -= OnSystemAudioCaptureFaulted;
        try { if (stopFirst && capture.IsCapturing) await capture.StopAsync(token).ConfigureAwait(false); }
        finally { await capture.DisposeAsync().ConfigureAwait(false); }
    }
}
