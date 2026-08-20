using System.Runtime.InteropServices;
using System.Diagnostics;
using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Windows.Capture;

/// <summary>
/// WASAPI-based microphone capture implementation. Handles buffer management, level metering, and WAV output.
/// Converts audio to PCM16 for reliable WAV writing and RMS calculation.
/// </summary>
internal sealed class WasapiAudioCapture : IAudioCaptureProvider
{
    private IMMDevice? _device;
    private readonly string _outputPath;
    private readonly WasapiCaptureMode _mode;
    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private IntPtr _captureEventHandle = IntPtr.Zero;
    private WaveFormatEx _waveFormat;
    private IntPtr _nativeFormatPtr = IntPtr.Zero; // Keep native descriptor alive until after Initialize
    private AudioFormatConverter.AudioFormat _sampleFormat;
    private uint _bufferFrameCount;
    private Pcm16WavWriter? _wavWriter;
    private long _framePosition;
    private readonly Stopwatch _captureClock = new();
    private bool _isCapturing;
    private bool _disposed;
    private Task? _captureThread;
    private CancellationTokenSource? _cancellationSource;

    public event EventHandler<AudioCaptureStartedEventArgs>? CaptureStarted;
    public event EventHandler<AudioFrameCapturedEventArgs>? FrameCaptured;
    public event EventHandler<AudioCaptureFaultEventArgs>? CaptureFaulted;

    public bool IsCapturing => _isCapturing;

    public WasapiAudioCapture(IMMDevice device, string outputPath, WasapiCaptureMode mode)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        _mode = mode;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WasapiAudioCapture));
        if (_isCapturing)
            throw new InvalidOperationException("Capture is already active.");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Keep device activation and IAudioClient initialization in the COM apartment
            // where the IMMDevice was obtained. The long-running buffer loop moves to a
            // worker thread only after all COM interfaces are initialized.
            InitializeCapture();
            _captureClock.Restart();
            _isCapturing = true;

            _cancellationSource = new CancellationTokenSource();
            _captureThread = RunCaptureLoopAsync(_cancellationSource.Token);

            CaptureStarted?.Invoke(this, new((int)_waveFormat.SampleRate, _waveFormat.Channels, 16)); // Always report 16-bit after conversion
            return Task.CompletedTask;
        }
        catch
        {
            CleanupResources();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isCapturing)
            throw new InvalidOperationException("Capture is not active.");

        _isCapturing = false;

        if (_cancellationSource != null)
        {
            _cancellationSource.Cancel();
        }

        if (_captureThread != null)
        {
            try
            {
                await _captureThread.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
        }

        _captureClock.Stop();
        FillSilenceUntil((long)(_captureClock.Elapsed.TotalSeconds * _waveFormat.SampleRate));

        try
        {
            _audioClient?.Stop();
        }
        catch { /* Ignore errors during stop */ }

        FinalizeWavFile();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        if (_isCapturing)
        {
            try
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch { /* Ignore errors during disposal */ }
        }

        CleanupResources();
        _disposed = true;
    }

    private void InitializeCapture()
    {
        try
        {
            // Activate IAudioClient from device with proper CLSCTX
            var device = _device ?? throw new ObjectDisposedException(nameof(WasapiAudioCapture));
            var hresult = device.Activate(typeof(IAudioClient).GUID, (uint)ComClassContext.All, IntPtr.Zero, out var audioClientObj);
            if (hresult < 0)
                throw new InvalidOperationException($"Failed to activate IAudioClient (HRESULT: 0x{hresult:X8}). Check that audio drivers are installed.");

            _audioClient = (IAudioClient?)audioClientObj ?? throw new InvalidOperationException("IAudioClient is null");

            // Get the mix format - keep pointer alive until after Initialize
            hresult = _audioClient.GetMixFormat(out _nativeFormatPtr);
            if (hresult < 0)
                throw new InvalidOperationException($"Failed to get mix format (HRESULT: 0x{hresult:X8})");

            if (_nativeFormatPtr == IntPtr.Zero)
                throw new InvalidOperationException("Mix format pointer is null");

            try
            {
                // Read format, handling WAVEFORMATEXTENSIBLE correctly
                _waveFormat = Marshal.PtrToStructure<WaveFormatEx>(_nativeFormatPtr);

                // Determine actual sample format from FormatTag or ExtensibleFormat SubFormat
                if (_waveFormat.FormatTag == WaveFormatExtensible.WAVE_FORMAT_EXTENSIBLE)
                {
                    var extFormat = Marshal.PtrToStructure<WaveFormatExtensible>(_nativeFormatPtr);
                    // Verify supported formats via SubFormat GUID
                    if (!extFormat.IsPcm && !extFormat.IsIeeeFloat)
                    {
                        throw new InvalidOperationException($"Unsupported audio format GUID: {extFormat.SubFormat}");
                    }
                    // Store the sample format based on SubFormat
                    _sampleFormat = extFormat.IsPcm ?
                        AudioFormatConverter.DetermineFormat(1, _waveFormat.BitsPerSample) :
                        (extFormat.IsIeeeFloat ? AudioFormatConverter.AudioFormat.IeeeFloat32 : AudioFormatConverter.AudioFormat.Unknown);
                }
                else if (_waveFormat.FormatTag == WasapiConstants.WAVE_FORMAT_PCM)
                {
                    _sampleFormat = AudioFormatConverter.DetermineFormat(1, _waveFormat.BitsPerSample);
                }
                else if (_waveFormat.FormatTag == WasapiConstants.WAVE_FORMAT_IEEE_FLOAT)
                {
                    _sampleFormat = AudioFormatConverter.AudioFormat.IeeeFloat32;
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported audio format tag: {_waveFormat.FormatTag:X4}");
                }

                // Initialize the audio client in shared mode with event-driven capture
                _captureEventHandle = CreateEventW(IntPtr.Zero, false, false, null);
                if (_captureEventHandle == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to create capture event");

                hresult = _audioClient.Initialize(
                    AudioClientShareMode.Shared,
                    (AudioClientStreamFlags)WasapiCaptureConfiguration.GetStreamFlags(_mode),
                    10000000, // 1 second buffer duration in 100ns units
                    0,        // Periodicity (ignored for shared mode)
                    _nativeFormatPtr,
                    IntPtr.Zero); // No specific session GUID

                if (hresult < 0)
                    throw new InvalidOperationException($"Failed to initialize AudioClient (HRESULT: 0x{hresult:X8})");

                hresult = _audioClient.SetEventHandle(_captureEventHandle);
                if (hresult < 0)
                    throw new InvalidOperationException($"Failed to set event handle (HRESULT: 0x{hresult:X8})");

                // Get capture client interface
                hresult = _audioClient.GetService(WasapiConstants.IID_IAudioCaptureClient, out var captureClientObj);
                if (hresult < 0)
                    throw new InvalidOperationException($"Failed to get capture client (HRESULT: 0x{hresult:X8})");

                _captureClient = (IAudioCaptureClient?)captureClientObj ?? throw new InvalidOperationException("IAudioCaptureClient is null");

                // Get buffer size
                hresult = _audioClient.GetBufferSize(out _bufferFrameCount);
                if (hresult < 0)
                    throw new InvalidOperationException($"Failed to get buffer size (HRESULT: 0x{hresult:X8})");

                // Initialize WAV file (PCM16 output)
                InitializeWavFile();

                // Start audio engine
                hresult = _audioClient.Start();
                if (hresult < 0)
                    throw new InvalidOperationException($"Failed to start audio client (HRESULT: 0x{hresult:X8})");
            }
            finally
            {
                // Free the native format pointer after Initialize
                if (_nativeFormatPtr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(_nativeFormatPtr);
                    _nativeFormatPtr = IntPtr.Zero;
                }
            }
        }
        catch (Exception ex)
        {
            CleanupResources();
            var source = _mode == WasapiCaptureMode.Loopback ? "System audio" : "Microphone";
            throw new InvalidOperationException($"{source} capture initialization failed", ex);
        }
    }

    private Task RunCaptureLoopAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => RunCaptureLoopInternal(cancellationToken), cancellationToken);
    }

    private void RunCaptureLoopInternal(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _isCapturing)
            {
                // Wait for the capture event
                var waitResult = WaitForSingleObject(_captureEventHandle, 1000); // 1 second timeout
                if (waitResult == WaitResult.Timeout)
                    continue;

                if (waitResult != WaitResult.Object0)
                    throw new InvalidOperationException($"WaitForSingleObject failed (result: {waitResult})");

                var hresult = _captureClient!.GetNextPacketSize(out var packetFrames);
                if (hresult < 0)
                    throw new InvalidOperationException(WasapiError.Describe("Failed to query capture packet size", hresult));

                while (packetFrames > 0 && !cancellationToken.IsCancellationRequested && _isCapturing)
                {
                    IntPtr pData = IntPtr.Zero;
                    uint numFrames = 0;
                    AudioClientBufferFlags bufferFlags = AudioClientBufferFlags.None;

                    hresult = _captureClient.GetBuffer(out pData, out numFrames, out bufferFlags, out _, out _);
                    if (hresult < 0)
                        throw new InvalidOperationException(WasapiError.Describe("Failed to get capture buffer", hresult));

                    try
                    {
                        var elapsedFrames = (long)(_captureClock.Elapsed.TotalSeconds * _waveFormat.SampleRate);
                        FillSilenceUntil(AudioTimeline.GetTargetFrameBeforePacket(_framePosition, elapsedFrames, numFrames));

                        // Handle SILENT flag — don't dereference null pointer
                        if ((bufferFlags & AudioClientBufferFlags.Silent) == 0 && pData != IntPtr.Zero)
                        {
                            // Process audio frames
                            int bytesRead = (int)(numFrames * _waveFormat.BlockAlign);
                            byte[] managedBuffer = new byte[bytesRead];
                            Marshal.Copy(pData, managedBuffer, 0, bytesRead);

                            // Convert to PCM16 if necessary and write to file
                            // Note: sample count = frames * channels, not just frames
                            int sampleCount = (int)numFrames * _waveFormat.Channels;
                            byte[] pcm16Buffer = ConvertToPcm16(managedBuffer, sampleCount);
                            _wavWriter?.Write(pcm16Buffer);

                            // Calculate level (works on PCM16)
                            var level = CalculateAudioLevel(pcm16Buffer);
                            _framePosition += (long)numFrames;

                            FrameCaptured?.Invoke(this, new(level, _framePosition - (long)numFrames, (int)numFrames, pcm16Buffer));
                        }
                        else if ((bufferFlags & AudioClientBufferFlags.Silent) != 0)
                        {
                            // Silent frame — write silence to file
                            int silentBytes = (int)(numFrames * 2 * _waveFormat.Channels); // PCM16 = 2 bytes per sample
                            byte[] silentBuffer = new byte[silentBytes];
                            _wavWriter?.Write(silentBuffer);

                            var level = AudioLevel.Silent;
                            _framePosition += (long)numFrames;
                            FrameCaptured?.Invoke(this, new(level, _framePosition - (long)numFrames, (int)numFrames, silentBuffer));
                        }
                    }
                    finally
                    {
                        // Always release the buffer
                        hresult = _captureClient.ReleaseBuffer(numFrames);
                        if (hresult < 0)
                            throw new InvalidOperationException(WasapiError.Describe("Failed to release capture buffer", hresult));
                    }

                    hresult = _captureClient.GetNextPacketSize(out packetFrames);
                    if (hresult < 0)
                        throw new InvalidOperationException(WasapiError.Describe("Failed to query capture packet size", hresult));
                }
            }
        }
        catch (Exception ex)
        {
            CaptureFaulted?.Invoke(this, new($"Audio capture loop failed: {ex.Message}", ex));
            _isCapturing = false;
        }
    }

    private byte[] ConvertToPcm16(byte[] sourceBuffer, int sampleCount)
    {
        // Use the sample format determined during initialization (which reads SubFormat GUID if EXTENSIBLE)
        return AudioFormatConverter.ConvertToPcm16(sourceBuffer, _sampleFormat, sampleCount);
    }

    private void FillSilenceUntil(long targetFramePosition)
    {
        var missingFrames = AudioTimeline.GetMissingFrames(_framePosition, targetFramePosition);
        if (missingFrames == 0 || _wavWriter is null) return;

        var framesPerChunk = Math.Max(1, (int)_waveFormat.SampleRate);
        while (missingFrames > 0)
        {
            var frames = (int)Math.Min(missingFrames, framesPerChunk);
            _wavWriter.Write(new byte[checked(frames * _waveFormat.Channels * 2)]);
            _framePosition += frames;
            missingFrames -= frames;
        }
    }

    private void InitializeWavFile()
    {
        try
        {
            _wavWriter = new(_outputPath, _waveFormat.Channels, _waveFormat.SampleRate);
            _framePosition = 0;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to initialize WAV file at {_outputPath}", ex);
        }
    }

    private void FinalizeWavFile()
    {
        var writer = Interlocked.Exchange(ref _wavWriter, null);
        if (writer is null) return;
        try
        {
            writer.FinalizeFile();
        }
        catch (Exception ex)
        {
            CaptureFaulted?.Invoke(this, new($"Failed to finalize WAV file: {ex.Message}", ex));
        }
        finally
        {
            writer.Dispose();
        }
    }

    private AudioLevel CalculateAudioLevel(byte[] pcm16Buffer)
    {
        if (pcm16Buffer.Length == 0)
            return AudioLevel.Silent;

        double sumSquares = 0;
        int sampleCount = pcm16Buffer.Length / 2;

        for (int i = 0; i < pcm16Buffer.Length; i += 2)
        {
            short sample = BitConverter.ToInt16(pcm16Buffer, i);
            double normalized = sample / 32768.0;
            sumSquares += normalized * normalized;
        }

        double rms = Math.Sqrt(sumSquares / sampleCount);
        double rmsDb = 20 * Math.Log10(Math.Max(rms, 1e-10)); // Avoid log(0)
        double peak = rms * 2; // Rough approximation

        return new(peak, rmsDb, sampleCount);
    }

    private void CleanupResources()
    {
        _isCapturing = false;
        _captureClock.Stop();

        _cancellationSource?.Cancel();
        _cancellationSource?.Dispose();
        _cancellationSource = null;

        try
        {
            _audioClient?.Stop();
        }
        catch { /* Ignore */ }

        if (_captureEventHandle != IntPtr.Zero)
        {
            CloseHandle(_captureEventHandle);
            _captureEventHandle = IntPtr.Zero;
        }

        Release(_captureClient);
        _captureClient = null;

        Release(_audioClient);
        _audioClient = null;

        var device = _device;
        _device = null;
        Release(device);

        FinalizeWavFile();
    }

    private static void Release(object? comObject)
    {
        if (comObject != null && Marshal.IsComObject(comObject))
        {
            try
            {
                Marshal.FinalReleaseComObject(comObject);
            }
            catch { /* Ignore COM errors during cleanup */ }
        }
    }

    #region Windows API

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern WaitResult WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    private enum WaitResult : uint
    {
        Object0 = 0,
        Timeout = 0x00000102
    }

    #endregion
}
