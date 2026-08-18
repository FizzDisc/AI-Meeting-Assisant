using System.Diagnostics;
using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Windows.Capture;

public sealed class CombinedCaptureCoordinator : ICaptureCoordinator
{
    private readonly string _baseDirectory;
    private readonly Func<string, string, IScreenCaptureProvider>? _screenFactory;
    private readonly Func<string, string, WasapiCaptureMode, IAudioCaptureProvider>? _audioFactory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Stopwatch _clock = new();
    private ScreenCaptureCoordinator? _screen;
    private DualAudioCaptureCoordinator? _audio;
    private CaptureSessionManifest? _manifest;
    private string? _manifestPath;
    private string? _sessionDirectory;
    private string? _screenPath;
    private string? _systemPath;
    private string? _microphonePath;
    private double? _screenOffset;
    private double? _systemOffset;
    private double? _microphoneOffset;
    private bool _screenStarted;
    private bool _audioStarted;

    public CombinedCaptureCoordinator(string captureBaseDirectory = "artifacts/captures", Func<string, string, IScreenCaptureProvider>? screenProviderFactory = null, Func<string, string, WasapiCaptureMode, IAudioCaptureProvider>? audioProviderFactory = null)
    {
        _baseDirectory = captureBaseDirectory;
        _screenFactory = screenProviderFactory;
        _audioFactory = audioProviderFactory;
    }

    public event EventHandler<AudioFrameCapturedEventArgs>? SystemAudioLevelChanged;
    public event EventHandler<AudioCaptureFaultEventArgs>? SystemAudioFaulted;
    public event EventHandler<AudioFrameCapturedEventArgs>? MicrophoneLevelChanged;
    public event EventHandler<AudioCaptureFaultEventArgs>? MicrophoneFaulted;
    public event EventHandler<CaptureErrorEventArgs>? CaptureFailed;
    public CaptureAlignmentManifest? LastAlignment { get; private set; }

    public async Task StartAsync(CapturePlan plan, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_manifest is not null) throw new InvalidOperationException("Capture session is already active.");
            LastAlignment = null;
            if (string.IsNullOrWhiteSpace(plan.ScreenSourceId) || string.IsNullOrWhiteSpace(plan.SystemAudioSourceId) || string.IsNullOrWhiteSpace(plan.MicrophoneSourceId))
                throw new ArgumentException("Screen, system audio and microphone source IDs are required.");

            var startedAt = DateTimeOffset.UtcNow;
            var timestamp = startedAt.LocalDateTime;
            var sessionId = $"session_{timestamp:yyyyMMdd_HHmmss_fff}";
            var directory = CaptureFileNaming.CreateUniqueDirectory(_baseDirectory, sessionId);
            _sessionDirectory = directory;
            _manifestPath = Path.Combine(directory, "manifest.json");
            DateTime Timestamp() => timestamp;
            IScreenCaptureProvider CreateScreen(string id, string path) { _screenPath = path; return _screenFactory?.Invoke(id, path) ?? new ScreenRecorderCaptureProvider(id, path); }
            IAudioCaptureProvider CreateAudio(string id, string path, WasapiCaptureMode mode)
            {
                if (mode is WasapiCaptureMode.Loopback) _systemPath = path; else _microphonePath = path;
                return _audioFactory?.Invoke(id, path, mode) ?? new WasapiAudioCaptureProvider(id, path, mode);
            }

            _screen = new(directory, CreateScreen, Timestamp);
            _audio = new(directory, CreateAudio, Timestamp);
            Subscribe(_screen, _audio);
            _manifest = new(2, sessionId, "preparing", startedAt, null, null, plan, []);
            WriteManifest();
            _clock.Restart();
            await _screen.StartAsync(plan, cancellationToken).ConfigureAwait(false);
            _screenStarted = true;
            await _audio.StartAsync(plan, cancellationToken).ConfigureAwait(false);
            _audioStarted = true;
            _manifest = BuildManifest("recording", null);
            WriteManifest();
        }
        catch
        {
            await CleanupAsync("failed", CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally { _lock.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await CleanupAsync("completed", cancellationToken).ConfigureAwait(false); }
        finally { _lock.Release(); }
    }

    private void Subscribe(ScreenCaptureCoordinator screen, DualAudioCaptureCoordinator audio)
    {
        screen.CaptureStarted += OnScreenStarted; screen.CaptureFailed += OnCaptureFailed;
        audio.SystemAudioStarted += OnSystemStarted; audio.MicrophoneStarted += OnMicrophoneStarted; audio.CaptureFailed += OnCaptureFailed;
        audio.SystemAudioLevelChanged += OnSystemLevel; audio.SystemAudioFaulted += OnSystemFault;
        audio.MicrophoneLevelChanged += OnMicrophoneLevel; audio.MicrophoneFaulted += OnMicrophoneFault;
    }

    private void Unsubscribe(ScreenCaptureCoordinator? screen, DualAudioCaptureCoordinator? audio)
    {
        if (screen is not null) { screen.CaptureStarted -= OnScreenStarted; screen.CaptureFailed -= OnCaptureFailed; }
        if (audio is null) return;
        audio.SystemAudioStarted -= OnSystemStarted; audio.MicrophoneStarted -= OnMicrophoneStarted; audio.CaptureFailed -= OnCaptureFailed;
        audio.SystemAudioLevelChanged -= OnSystemLevel; audio.SystemAudioFaulted -= OnSystemFault;
        audio.MicrophoneLevelChanged -= OnMicrophoneLevel; audio.MicrophoneFaulted -= OnMicrophoneFault;
    }

    private void OnScreenStarted(object? s, EventArgs e) => _screenOffset ??= _clock.Elapsed.TotalMilliseconds;
    private void OnSystemStarted(object? s, AudioCaptureStartedEventArgs e) => _systemOffset ??= _clock.Elapsed.TotalMilliseconds;
    private void OnMicrophoneStarted(object? s, AudioCaptureStartedEventArgs e) => _microphoneOffset ??= _clock.Elapsed.TotalMilliseconds;
    private void OnCaptureFailed(object? s, CaptureErrorEventArgs e) => CaptureFailed?.Invoke(this, e);
    private void OnSystemLevel(object? s, AudioFrameCapturedEventArgs e) => SystemAudioLevelChanged?.Invoke(this, e);
    private void OnSystemFault(object? s, AudioCaptureFaultEventArgs e) => SystemAudioFaulted?.Invoke(this, e);
    private void OnMicrophoneLevel(object? s, AudioFrameCapturedEventArgs e) => MicrophoneLevelChanged?.Invoke(this, e);
    private void OnMicrophoneFault(object? s, AudioCaptureFaultEventArgs e) => MicrophoneFaulted?.Invoke(this, e);

    private CaptureSessionManifest BuildManifest(string status, DateTimeOffset? completedAt)
    {
        var streams = new List<CaptureStreamManifest>();
        AddStream(streams, "screen", _screenPath, _screenOffset);
        AddStream(streams, "system_audio", _systemPath, _systemOffset);
        AddStream(streams, "microphone", _microphonePath, _microphoneOffset);
        return _manifest! with { Status = status, CompletedAtUtc = completedAt, DurationMilliseconds = completedAt is null ? null : _clock.Elapsed.TotalMilliseconds, Streams = streams };
    }

    private static void AddStream(List<CaptureStreamManifest> streams, string kind, string? path, double? offset)
    {
        if (path is not null) streams.Add(new(kind, Path.GetFileName(path), offset ?? 0));
    }

    private void WriteManifest() { if (_manifestPath is not null && _manifest is not null) CaptureSessionManifestStore.WriteAtomic(_manifestPath, _manifest); }

    private async Task CleanupAsync(string finalStatus, CancellationToken token)
    {
        var screen = _screen; var audio = _audio; var stopAudio = _audioStarted; var stopScreen = _screenStarted;
        _audioStarted = _screenStarted = false;
        Exception? error = null;
        if (stopAudio && audio is not null) { try { await audio.StopAsync(token).ConfigureAwait(false); } catch (Exception ex) { error = ex; } }
        if (stopScreen && screen is not null) { try { await screen.StopAsync(token).ConfigureAwait(false); } catch (Exception ex) { error ??= ex; } }
        _clock.Stop();
        if (_manifest is not null)
        {
            _manifest = BuildManifest(error is null ? finalStatus : "failed", DateTimeOffset.UtcNow);
            if (error is null && finalStatus == "completed" && _sessionDirectory is not null)
                _manifest = CaptureAlignmentAnalyzer.Analyze(_sessionDirectory, _manifest);
            LastAlignment = _manifest.Alignment;
            WriteManifest();
        }
        Unsubscribe(screen, audio);
        _screen = null; _audio = null; _manifest = null; _manifestPath = null; _sessionDirectory = null;
        _screenPath = _systemPath = _microphonePath = null;
        _screenOffset = _systemOffset = _microphoneOffset = null;
        if (error is not null) throw error;
    }
}
