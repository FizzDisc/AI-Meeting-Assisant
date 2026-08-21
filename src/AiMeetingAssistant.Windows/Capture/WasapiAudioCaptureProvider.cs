using System.Runtime.InteropServices;
using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Windows.Capture;

/// <summary>
/// Wraps WasapiAudioCapture to provide the Core IAudioCaptureProvider interface.
/// Handles device selection and output path management.
/// </summary>
internal sealed class WasapiAudioCaptureProvider : IAudioCaptureProvider, IAudioCaptureSuppression
{
    private readonly string _deviceId;
    private readonly string _outputPath;
    private readonly WasapiCaptureMode _mode;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private WasapiAudioCapture? _capture;

    public event EventHandler<AudioCaptureStartedEventArgs>? CaptureStarted;
    public event EventHandler<AudioFrameCapturedEventArgs>? FrameCaptured;
    public event EventHandler<AudioCaptureFaultEventArgs>? CaptureFaulted;

    public bool IsCapturing => _capture?.IsCapturing ?? false;
    public bool IsAudioSuppressed { get; private set; }

    public void SetAudioSuppressed(bool suppressed)
    {
        IsAudioSuppressed = suppressed;
        _capture?.SetAudioSuppressed(suppressed);
    }

    public WasapiAudioCaptureProvider(string deviceId, string outputPath, WasapiCaptureMode mode = WasapiCaptureMode.Input)
    {
        _deviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
        _outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        _mode = mode;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var nativeDeviceId = ExtractNativeDeviceId(_deviceId);

            var device = GetDeviceById(nativeDeviceId);
            if (device == null)
                throw new InvalidOperationException($"Audio endpoint not found: {_deviceId}");

            _capture = new(device, _outputPath, _mode);
            _capture.SetAudioSuppressed(IsAudioSuppressed);
            _capture.CaptureStarted += OnCaptureCaptureStarted;
            _capture.FrameCaptured += OnCaptureFrameCaptured;
            _capture.CaptureFaulted += OnCaptureCaptureFaulted;

            await _capture.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await CleanupAsync().ConfigureAwait(false);
            var source = _mode == WasapiCaptureMode.Loopback ? "system audio" : "microphone";
            throw new InvalidOperationException($"Failed to start {source} capture", ex);
        }
    }

    private static string ExtractNativeDeviceId(string sourceId)
    {
        foreach (var prefix in new[] { "microphone:", "system-audio:" })
        {
            if (sourceId.StartsWith(prefix, StringComparison.Ordinal))
                return sourceId[prefix.Length..];
        }

        return sourceId;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var capture = DetachCapture();
            if (capture is null) return;
            if (capture.IsCapturing) await capture.StopAsync(cancellationToken).ConfigureAwait(false);
            await capture.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CleanupAsync().ConfigureAwait(false);
    }

    private void OnCaptureCaptureStarted(object? sender, AudioCaptureStartedEventArgs e)
    {
        CaptureStarted?.Invoke(this, e);
    }

    private void OnCaptureFrameCaptured(object? sender, AudioFrameCapturedEventArgs e)
    {
        FrameCaptured?.Invoke(this, e);
    }

    private void OnCaptureCaptureFaulted(object? sender, AudioCaptureFaultEventArgs e)
    {
        CaptureFaulted?.Invoke(this, e);
    }

    private async Task CleanupAsync()
    {
        var capture = DetachCapture();
        if (capture is not null) await capture.DisposeAsync().ConfigureAwait(false);
    }

    private WasapiAudioCapture? DetachCapture()
    {
        var capture = _capture;
        _capture = null;
        if (capture is null) return null;
        capture.CaptureStarted -= OnCaptureCaptureStarted;
        capture.FrameCaptured -= OnCaptureFrameCaptured;
        capture.CaptureFaulted -= OnCaptureCaptureFaulted;
        return capture;
    }

    private static IMMDevice? GetDeviceById(string deviceId)
    {
        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(new("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!;
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
            try
            {
                var hresult = enumerator.GetDevice(deviceId, out var device);
                if (hresult < 0)
                    return null;
                return device;
            }
            finally
            {
                Marshal.FinalReleaseComObject(enumerator);
            }
        }
        catch
        {
            return null;
        }
    }
}
