using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Windows.Capture;

public sealed class CombinedCaptureCoordinator : ICaptureCoordinator
{
    private readonly ScreenCaptureCoordinator _screenCapture;
    private readonly DualAudioCaptureCoordinator _audioCapture;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private DateTime? _sessionTimestamp;
    private bool _screenStarted;
    private bool _audioStarted;

    public CombinedCaptureCoordinator(
        string captureBaseDirectory = "artifacts/captures",
        Func<string, string, IScreenCaptureProvider>? screenProviderFactory = null,
        Func<string, string, WasapiCaptureMode, IAudioCaptureProvider>? audioProviderFactory = null)
    {
        DateTime GetSessionTimestamp() => _sessionTimestamp
            ?? throw new InvalidOperationException("No combined capture session timestamp is active.");

        _screenCapture = new(captureBaseDirectory, screenProviderFactory, GetSessionTimestamp);
        _audioCapture = new(captureBaseDirectory, audioProviderFactory, GetSessionTimestamp);
        _screenCapture.CaptureFailed += OnCaptureFailed;
        _audioCapture.CaptureFailed += OnCaptureFailed;
        _audioCapture.SystemAudioLevelChanged += (sender, args) => SystemAudioLevelChanged?.Invoke(this, args);
        _audioCapture.SystemAudioFaulted += (sender, args) => SystemAudioFaulted?.Invoke(this, args);
        _audioCapture.MicrophoneLevelChanged += (sender, args) => MicrophoneLevelChanged?.Invoke(this, args);
        _audioCapture.MicrophoneFaulted += (sender, args) => MicrophoneFaulted?.Invoke(this, args);
    }

    public event EventHandler<AudioFrameCapturedEventArgs>? SystemAudioLevelChanged;
    public event EventHandler<AudioCaptureFaultEventArgs>? SystemAudioFaulted;
    public event EventHandler<AudioFrameCapturedEventArgs>? MicrophoneLevelChanged;
    public event EventHandler<AudioCaptureFaultEventArgs>? MicrophoneFaulted;
    public event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

    public async Task StartAsync(CapturePlan plan, CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessionTimestamp is not null) throw new InvalidOperationException("Capture session is already active.");
            if (string.IsNullOrWhiteSpace(plan.ScreenSourceId) ||
                string.IsNullOrWhiteSpace(plan.SystemAudioSourceId) ||
                string.IsNullOrWhiteSpace(plan.MicrophoneSourceId))
                throw new ArgumentException("Screen, system audio and microphone source IDs are required.");

            _sessionTimestamp = DateTime.Now;
            await _screenCapture.StartAsync(plan, cancellationToken).ConfigureAwait(false);
            _screenStarted = true;
            await _audioCapture.StartAsync(plan, cancellationToken).ConfigureAwait(false);
            _audioStarted = true;
        }
        catch
        {
            await CleanupCoreAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally { _lifecycleLock.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await CleanupCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _lifecycleLock.Release(); }
    }

    private void OnCaptureFailed(object? sender, CaptureErrorEventArgs e) => CaptureFailed?.Invoke(this, e);

    private async Task CleanupCoreAsync(CancellationToken cancellationToken)
    {
        var stopAudio = _audioStarted;
        var stopScreen = _screenStarted;
        _audioStarted = false;
        _screenStarted = false;
        _sessionTimestamp = null;

        Exception? firstError = null;
        if (stopAudio)
        {
            try { await _audioCapture.StopAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { firstError = ex; }
        }
        if (stopScreen)
        {
            try { await _screenCapture.StopAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { firstError ??= ex; }
        }
        if (firstError is not null) throw firstError;
    }
}
