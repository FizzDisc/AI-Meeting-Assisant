using AiMeetingAssistant.Core.Capture;
using ScreenRecorderLib;

namespace AiMeetingAssistant.Windows.Capture;

public sealed class ScreenRecorderCaptureProvider : IScreenCaptureProvider
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);
    private readonly string _deviceName;
    private readonly string _outputPath;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private Recorder? _recorder;
    private TaskCompletionSource? _started;
    private TaskCompletionSource? _stopped;
    private bool _isCapturing;

    public ScreenRecorderCaptureProvider(string screenSourceId, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenSourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        _deviceName = screenSourceId.StartsWith("screen:", StringComparison.OrdinalIgnoreCase)
            ? screenSourceId["screen:".Length..]
            : screenSourceId;
        _outputPath = outputPath;
    }

    public event EventHandler<CaptureErrorEventArgs>? CaptureFaulted;

    public bool IsCapturing => _isCapturing;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_recorder is not null) throw new InvalidOperationException("Screen capture is already active.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_outputPath))!);

            var source = new DisplayRecordingSource(_deviceName);
            ScreenSize? outputSize = null;
            if (DisplaySourceEnumerator.TryGetDimensions(_deviceName, out var sourceWidth, out var sourceHeight))
            {
                var fitted = ScreenCaptureSizing.FitWithinEncoderLimit(sourceWidth, sourceHeight);
                outputSize = new ScreenSize(fitted.Width, fitted.Height);
            }
            var options = new RecorderOptions
            {
                SourceOptions = new SourceOptions { RecordingSources = [source] },
                OutputOptions = new OutputOptions
                {
                    RecorderMode = RecorderMode.Video,
                    OutputFrameSize = outputSize,
                    Stretch = StretchMode.Uniform
                },
                AudioOptions = new AudioOptions { IsAudioEnabled = false },
                VideoEncoderOptions = new VideoEncoderOptions
                {
                    Encoder = new H264VideoEncoder(),
                    Framerate = 30,
                    Bitrate = 8_000_000,
                    IsFixedFramerate = true,
                    IsHardwareEncodingEnabled = true,
                    IsMp4FastStartEnabled = false
                }
            };

            _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _recorder = Recorder.CreateRecorder(options);
            _recorder.OnStatusChanged += OnStatusChanged;
            _recorder.OnRecordingComplete += OnRecordingComplete;
            _recorder.OnRecordingFailed += OnRecordingFailed;
            _recorder.Record(_outputPath);

            await _started.Task.WaitAsync(StartTimeout, cancellationToken).ConfigureAwait(false);
            _isCapturing = true;
        }
        catch
        {
            DisposeRecorder();
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
            if (_recorder is null) return;
            _recorder.Stop();
            if (_stopped is not null)
                await _stopped.Task.WaitAsync(StopTimeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _isCapturing = false;
            DisposeRecorder();
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(CancellationToken.None).ConfigureAwait(false); }
        catch { DisposeRecorder(); }
        _lifecycleLock.Dispose();
    }

    private void OnStatusChanged(object? sender, RecordingStatusEventArgs e)
    {
        if (e.Status is RecorderStatus.Recording) _started?.TrySetResult();
    }

    private void OnRecordingComplete(object? sender, RecordingCompleteEventArgs e) => _stopped?.TrySetResult();

    private void OnRecordingFailed(object? sender, RecordingFailedEventArgs e)
    {
        var exception = new InvalidOperationException(e.Error);
        _started?.TrySetException(exception);
        _stopped?.TrySetException(exception);
        CaptureFaulted?.Invoke(this, new CaptureErrorEventArgs(e.Error, exception));
    }

    private void DisposeRecorder()
    {
        var recorder = _recorder;
        _recorder = null;
        if (recorder is null) return;
        recorder.OnStatusChanged -= OnStatusChanged;
        recorder.OnRecordingComplete -= OnRecordingComplete;
        recorder.OnRecordingFailed -= OnRecordingFailed;
        recorder.Dispose();
    }
}
