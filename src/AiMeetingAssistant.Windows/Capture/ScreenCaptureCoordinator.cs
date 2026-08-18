using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Windows.Capture;

public sealed class ScreenCaptureCoordinator : ICaptureCoordinator
{
    private readonly string _captureBaseDirectory;
    private readonly Func<string, string, IScreenCaptureProvider> _providerFactory;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private IScreenCaptureProvider? _capture;

    public ScreenCaptureCoordinator(
        string captureBaseDirectory = "artifacts/captures",
        Func<string, string, IScreenCaptureProvider>? providerFactory = null)
    {
        _captureBaseDirectory = captureBaseDirectory ?? throw new ArgumentNullException(nameof(captureBaseDirectory));
        _providerFactory = providerFactory ?? ((id, path) => new ScreenRecorderCaptureProvider(id, path));
    }

    public event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

    public async Task StartAsync(CapturePlan plan, CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_capture is not null) throw new InvalidOperationException("Capture session is already active.");
            if (string.IsNullOrWhiteSpace(plan.ScreenSourceId)) throw new ArgumentException("A screen source ID is required.");

            Directory.CreateDirectory(_captureBaseDirectory);
            var path = CaptureFileNaming.CreateUniqueMp4Path(_captureBaseDirectory, "screen", DateTime.Now);
            _capture = _providerFactory(plan.ScreenSourceId, path);
            _capture.CaptureFaulted += OnCaptureFaulted;
            await _capture.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await CleanupCoreAsync(stopFirst: true, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally { _lifecycleLock.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await CleanupCoreAsync(stopFirst: true, cancellationToken).ConfigureAwait(false); }
        finally { _lifecycleLock.Release(); }
    }

    private void OnCaptureFaulted(object? sender, CaptureErrorEventArgs e) => CaptureFailed?.Invoke(this, e);

    private async Task CleanupCoreAsync(bool stopFirst, CancellationToken token)
    {
        var capture = _capture;
        _capture = null;
        if (capture is null) return;
        capture.CaptureFaulted -= OnCaptureFaulted;
        try { if (stopFirst && capture.IsCapturing) await capture.StopAsync(token).ConfigureAwait(false); }
        finally { await capture.DisposeAsync().ConfigureAwait(false); }
    }
}
