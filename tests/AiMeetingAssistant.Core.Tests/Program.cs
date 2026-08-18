using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Core.Recording;

var tests = new (string Name, Func<Task> Run)[]
{
    ("happy path follows all transitions", HappyPathFollowsAllTransitions),
    ("stop from idle is rejected", StopFromIdleIsRejected),
    ("capture start failure moves session to failed", StartFailureMovesSessionToFailed),
    ("completed session can start again", CompletedSessionCanStartAgain),
    ("recording state includes capture plan", RecordingStateIncludesCapturePlan),
    ("device loss during recording captures error", DeviceLossCapturesToFailed),
    ("quick start-stop cycle works", QuickStartStopCycleWorks),
    ("audio level calculation rejects negative dB", AudioLevelCalculationIsValid),
    ("audio format converter: PCM16 pass-through", AudioFormatConverter_Pcm16PassThrough),
    ("audio format converter: Float32 conversion", AudioFormatConverter_Float32Conversion),
    ("audio format converter: Float32 clamping", AudioFormatConverter_Float32Clamping),
    ("audio format converter: PCM24 conversion", AudioFormatConverter_Pcm24Conversion),
    ("audio format converter: stereo Float32 preserves channels", AudioFormatConverter_StereoFloat32),
    ("audio format converter: stereo PCM24 preserves channels", AudioFormatConverter_StereoPcm24),
    ("audio format converter: unknown format throws", AudioFormatConverter_UnknownFormatThrows),
    ("WAV header consistency", WavHeaderConsistency),
    ("capture fault state transition", CaptureFaultStateTransition)
    ,("capture fault cleans up exactly once", CaptureFaultCleansUpExactlyOnce)
    ,("fault and stop race completes without deadlock", FaultAndStopRaceCompletes)
    ,("shutdown after failed capture is idempotent", ShutdownAfterFailureIsIdempotent)
    ,("system audio filename is unique and correctly prefixed", SystemAudioFilenameIsUnique)
    ,("screen filename is an MP4 and correctly prefixed", ScreenFilenameIsMp4)
    ,("session manifest is written atomically", SessionManifestIsWrittenAtomically)
    ,("audio timeline fills missing silent frames", AudioTimelineFillsMissingFrames)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

return failures == 0 ? 0 : 1;

static async Task HappyPathFollowsAllTransitions()
{
    var states = new List<RecordingSessionState>();
    var session = new RecordingSession(new FakeCaptureCoordinator());
    session.StateChanged += (_, args) => states.Add(args.CurrentState);

    await session.StartAsync(TestPlan());
    await session.StopAsync();

    Equal(RecordingSessionState.Completed, session.State);
    SequenceEqual(
        [RecordingSessionState.Preparing, RecordingSessionState.Recording, RecordingSessionState.Stopping, RecordingSessionState.Completed],
        states);
}

static async Task StopFromIdleIsRejected()
{
    var session = new RecordingSession(new FakeCaptureCoordinator());
    await ThrowsAsync<InvalidOperationException>(() => session.StopAsync());
    Equal(RecordingSessionState.Idle, session.State);
}

static async Task StartFailureMovesSessionToFailed()
{
    var session = new RecordingSession(new FakeCaptureCoordinator(failOnStart: true));
    await ThrowsAsync<IOException>(() => session.StartAsync(TestPlan()));
    Equal(RecordingSessionState.Failed, session.State);
    Equal("Simulated start failure.", session.LastError);
}

static async Task CompletedSessionCanStartAgain()
{
    var session = new RecordingSession(new FakeCaptureCoordinator());
    await session.StartAsync(TestPlan());
    await session.StopAsync();
    await session.StartAsync(TestPlan());
    Equal(RecordingSessionState.Recording, session.State);
}

static async Task RecordingStateIncludesCapturePlan()
{
    var plan = new CapturePlan("screen-1", "system-audio-1", "mic-1");
    var coordinator = new FakeCaptureCoordinator();
    var session = new RecordingSession(coordinator);

    await session.StartAsync(plan);
    var lastPlan = coordinator.LastPlan ?? throw new InvalidOperationException("LastPlan was not set");
    Equal(plan.MicrophoneSourceId, lastPlan.MicrophoneSourceId);
    await session.StopAsync();
}

static async Task DeviceLossCapturesToFailed()
{
    var coordinator = new FakeCaptureCoordinator(failOnStop: true);
    var session = new RecordingSession(coordinator);

    await session.StartAsync(TestPlan());
    Equal(RecordingSessionState.Recording, session.State);

    await ThrowsAsync<IOException>(() => session.StopAsync());
    Equal(RecordingSessionState.Failed, session.State);
}

static async Task QuickStartStopCycleWorks()
{
    var session = new RecordingSession(new FakeCaptureCoordinator());

    for (int i = 0; i < 3; i++)
    {
        await session.StartAsync(TestPlan());
        Equal(RecordingSessionState.Recording, session.State);
        await session.StopAsync();
        Equal(RecordingSessionState.Completed, session.State);
    }
}

static Task AudioLevelCalculationIsValid()
{
    // AudioLevel.Silent should represent no signal
    var silent = AudioLevel.Silent;
    if (!(silent.RmsDb < -60))
    {
        throw new InvalidOperationException("Silent level should be very negative dB");
    }

    // Peak should never be NaN
    if (double.IsNaN(silent.PeakAmplitude))
    {
        throw new InvalidOperationException("Peak amplitude cannot be NaN");
    }

    return Task.CompletedTask;
}

static Task AudioFormatConverter_Pcm16PassThrough()
{
    byte[] input = { 0x00, 0x00, 0x01, 0x00 };
    var result = AudioFormatConverter.ConvertToPcm16(input, AudioFormatConverter.AudioFormat.Pcm16, sampleCount: 2);
    if (!input.SequenceEqual(result))
    {
        throw new InvalidOperationException("PCM16 pass-through failed");
    }
    return Task.CompletedTask;
}

static Task AudioFormatConverter_Float32Conversion()
{
    byte[] input = BitConverter.GetBytes(0.5f);
    var result = AudioFormatConverter.ConvertToPcm16(input, AudioFormatConverter.AudioFormat.IeeeFloat32, sampleCount: 1);

    short pcm16 = BitConverter.ToInt16(result, 0);
    // 0.5 * 32767 ≈ 16383
    if (pcm16 < 16380 || pcm16 > 16385)
    {
        throw new InvalidOperationException($"Float32 conversion failed: got {pcm16}");
    }
    return Task.CompletedTask;
}

static Task AudioFormatConverter_Float32Clamping()
{
    byte[] input = BitConverter.GetBytes(2.0f);
    var result = AudioFormatConverter.ConvertToPcm16(input, AudioFormatConverter.AudioFormat.IeeeFloat32, sampleCount: 1);

    short pcm16 = BitConverter.ToInt16(result, 0);
    if (pcm16 != 32767)
    {
        throw new InvalidOperationException($"Float32 clamping failed: expected 32767, got {pcm16}");
    }
    return Task.CompletedTask;
}

static Task AudioFormatConverter_Pcm24Conversion()
{
    byte[] input = { 0x01, 0x00, 0x00 };
    var result = AudioFormatConverter.ConvertToPcm16(input, AudioFormatConverter.AudioFormat.Pcm24, sampleCount: 1);

    short pcm16 = BitConverter.ToInt16(result, 0);
    if (pcm16 != 0)
    {
        throw new InvalidOperationException($"PCM24 conversion failed: expected 0, got {pcm16}");
    }
    return Task.CompletedTask;
}

static Task AudioFormatConverter_StereoFloat32()
{
    var samples = new[] { -1f, -0.5f, 0.25f, 1f };
    var input = samples.SelectMany(BitConverter.GetBytes).ToArray();
    var result = AudioFormatConverter.ConvertToPcm16(input, AudioFormatConverter.AudioFormat.IeeeFloat32, 4);
    Equal(8, result.Length);
    SequenceEqual(new short[] { -32767, -16383, 8191, 32767 }, ReadPcm16(result));
    return Task.CompletedTask;
}

static Task AudioFormatConverter_StereoPcm24()
{
    byte[] input = { 0x00, 0x00, 0x80, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x40, 0x00, 0xFF, 0x7F };
    var result = AudioFormatConverter.ConvertToPcm16(input, AudioFormatConverter.AudioFormat.Pcm24, 4);
    Equal(8, result.Length);
    SequenceEqual(new short[] { -32768, -16384, 16384, 32767 }, ReadPcm16(result));
    return Task.CompletedTask;
}

static IEnumerable<short> ReadPcm16(byte[] bytes) =>
    Enumerable.Range(0, bytes.Length / 2).Select(index => BitConverter.ToInt16(bytes, index * 2));

static Task AudioFormatConverter_UnknownFormatThrows()
{
    byte[] input = { };
    try
    {
        AudioFormatConverter.ConvertToPcm16(input, AudioFormatConverter.AudioFormat.Unknown, sampleCount: 0);
        throw new InvalidOperationException("Should have thrown for unknown format");
    }
    catch (InvalidOperationException ex)
    {
        if (!ex.Message.Contains("Unsupported"))
        {
            throw;
        }
    }
    return Task.CompletedTask;
}

static Task WavHeaderConsistency()
{
    string testFile = Path.Combine(Path.GetTempPath(), $"test_wav_production_{Guid.NewGuid()}.wav");
    try
    {
        using (var writer = new Pcm16WavWriter(testFile, channels: 2, sampleRate: 44100))
            writer.Write(new byte[400]);

        // Verify the finalized file structure
        ValidateWavFile(testFile, channels: 2, sampleRate: 44100, expectedDataSize: 400);

        return Task.CompletedTask;
    }
    finally
    {
        if (File.Exists(testFile))
            File.Delete(testFile);
    }

    static void ValidateWavFile(string filePath, int channels, uint sampleRate, long expectedDataSize)
    {
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            using (var reader = new BinaryReader(fs))
            {
                // Verify RIFF header
                byte[] riffHeader = reader.ReadBytes(4);
                if (!(riffHeader[0] == 'R' && riffHeader[1] == 'I' && riffHeader[2] == 'F' && riffHeader[3] == 'F'))
                    throw new InvalidOperationException("Invalid RIFF header");

                uint fileSize = reader.ReadUInt32();
                if (fileSize != fs.Length - 8)
                    throw new InvalidOperationException($"RIFF size mismatch: {fileSize} vs {fs.Length - 8}");

                byte[] waveHeader = reader.ReadBytes(4);
                if (!(waveHeader[0] == 'W' && waveHeader[1] == 'A' && waveHeader[2] == 'V' && waveHeader[3] == 'E'))
                    throw new InvalidOperationException("Invalid WAVE header");

                // Verify fmt sub-chunk
                byte[] fmtHeader = reader.ReadBytes(4);
                if (!(fmtHeader[0] == 'f' && fmtHeader[1] == 'm' && fmtHeader[2] == 't' && fmtHeader[3] == ' '))
                    throw new InvalidOperationException("Invalid fmt sub-chunk");

                uint fmtSize = reader.ReadUInt32();
                if (fmtSize != 16)
                    throw new InvalidOperationException($"fmt sub-chunk size should be 16, got {fmtSize}");

                ushort audioFormat = reader.ReadUInt16();
                if (audioFormat != 1)
                    throw new InvalidOperationException($"Expected PCM (1), got {audioFormat}");

                ushort actualChannels = reader.ReadUInt16();
                if (actualChannels != channels)
                    throw new InvalidOperationException($"Expected {channels} channels, got {actualChannels}");

                uint actualSampleRate = reader.ReadUInt32();
                if (actualSampleRate != sampleRate)
                    throw new InvalidOperationException($"Expected {sampleRate} Hz, got {actualSampleRate}");

                uint expectedByteRate = sampleRate * (uint)channels * 2;
                uint actualByteRate = reader.ReadUInt32();
                if (actualByteRate != expectedByteRate)
                    throw new InvalidOperationException($"Expected byte rate {expectedByteRate}, got {actualByteRate}");

                ushort expectedBlockAlign = (ushort)(channels * 2);
                ushort actualBlockAlign = reader.ReadUInt16();
                if (actualBlockAlign != expectedBlockAlign)
                    throw new InvalidOperationException($"Expected block align {expectedBlockAlign}, got {actualBlockAlign}");

                ushort bitsPerSample = reader.ReadUInt16();
                if (bitsPerSample != 16)
                    throw new InvalidOperationException($"Expected 16 bits per sample, got {bitsPerSample}");

                // Verify data sub-chunk
                byte[] dataHeader = reader.ReadBytes(4);
                if (!(dataHeader[0] == 'd' && dataHeader[1] == 'a' && dataHeader[2] == 't' && dataHeader[3] == 'a'))
                    throw new InvalidOperationException("Invalid data sub-chunk header");

                uint dataSize = reader.ReadUInt32();
                if (dataSize != expectedDataSize)
                    throw new InvalidOperationException($"Expected data size {expectedDataSize}, got {dataSize}");
            }
        }
    }
}

static async Task CaptureFaultCleansUpExactlyOnce()
{
    var coordinator = new TestFailingCaptureCoordinator();
    var session = new RecordingSession(coordinator);
    await session.StartAsync(TestPlan());
    coordinator.RaiseFault("device lost");
    await WaitForState(session, RecordingSessionState.Failed);
    Equal(1, coordinator.StopCount);
}

static async Task FaultAndStopRaceCompletes()
{
    var coordinator = new TestFailingCaptureCoordinator(stopDelay: TimeSpan.FromMilliseconds(25));
    var session = new RecordingSession(coordinator);
    await session.StartAsync(TestPlan());
    coordinator.RaiseFault("device lost");
    var stop = session.StopAsync();
    await Task.WhenAny(Task.WhenAll(IgnoreInvalidTransition(stop), WaitForState(session, RecordingSessionState.Failed)), Task.Delay(2000));
    if (session.State is not (RecordingSessionState.Failed or RecordingSessionState.Completed))
        throw new InvalidOperationException($"Race ended in {session.State}.");
    Equal(1, coordinator.StopCount);
}

static async Task ShutdownAfterFailureIsIdempotent()
{
    var coordinator = new TestFailingCaptureCoordinator();
    var session = new RecordingSession(coordinator);
    await session.StartAsync(TestPlan());
    coordinator.RaiseFault("device lost");
    await WaitForState(session, RecordingSessionState.Failed);
    await session.ShutdownAsync();
    await session.ShutdownAsync();
    Equal(3, coordinator.StopCallCount);
    Equal(1, coordinator.StopCount);
}

static Task SystemAudioFilenameIsUnique()
{
    var directory = Path.Combine(Path.GetTempPath(), $"aima_naming_{Guid.NewGuid():N}");
    try
    {
        var timestamp = new DateTime(2026, 8, 17, 12, 34, 56, 789);
        var first = CaptureFileNaming.CreateUniqueWavPath(directory, "system_audio", timestamp);
        Equal("system_audio_20260817_123456_789.wav", Path.GetFileName(first));
        File.WriteAllBytes(first, []);
        var second = CaptureFileNaming.CreateUniqueWavPath(directory, "system_audio", timestamp);
        Equal("system_audio_20260817_123456_789_01.wav", Path.GetFileName(second));
        return Task.CompletedTask;
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static Task ScreenFilenameIsMp4()
{
    var directory = Path.Combine(Path.GetTempPath(), $"aima_screen_naming_{Guid.NewGuid():N}");
    try
    {
        var timestamp = new DateTime(2026, 8, 18, 12, 34, 56, 789);
        var path = CaptureFileNaming.CreateUniqueMp4Path(directory, "screen", timestamp);
        Equal("screen_20260818_123456_789.mp4", Path.GetFileName(path));
        return Task.CompletedTask;
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static Task SessionManifestIsWrittenAtomically()
{
    var directory = Path.Combine(Path.GetTempPath(), $"aima_manifest_{Guid.NewGuid():N}");
    try
    {
        var path = Path.Combine(directory, "manifest.json");
        var manifest = new CaptureSessionManifest(1, "session-test", "recording", DateTimeOffset.UtcNow, null, null,
            new("screen", "system", "microphone"), [new("screen", "screen.mp4", 12.5)]);
        CaptureSessionManifestStore.WriteAtomic(path, manifest);
        var json = File.ReadAllText(path);
        if (!json.Contains("session-test", StringComparison.Ordinal) || !json.Contains("12.5", StringComparison.Ordinal))
            throw new InvalidOperationException("Manifest JSON is missing session timing data.");
        Equal(0, Directory.GetFiles(directory, "*.tmp").Length);
        return Task.CompletedTask;
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}

static Task AudioTimelineFillsMissingFrames()
{
    Equal(38400L, AudioTimeline.GetTargetFrameBeforePacket(currentFrame: 0, elapsedFrames: 48000, packetFrames: 9600));
    Equal(38400L, AudioTimeline.GetMissingFrames(currentFrame: 0, targetFrame: 38400));
    Equal(0L, AudioTimeline.GetMissingFrames(currentFrame: 48000, targetFrame: 38400));
    return Task.CompletedTask;
}

static async Task IgnoreInvalidTransition(Task task)
{
    try { await task; } catch (InvalidOperationException) { }
}

static async Task WaitForState(RecordingSession session, RecordingSessionState state)
{
    var timeout = DateTime.UtcNow.AddSeconds(2);
    while (session.State != state && DateTime.UtcNow < timeout) await Task.Delay(10);
    Equal(state, session.State);
}

static async Task CaptureFaultStateTransition()
{
    // Test that async capture faults move RecordingSession to Failed
    var coordinator = new TestFailingCaptureCoordinator();
    var session = new RecordingSession(coordinator);

    // Start recording (succeeds)
    await session.StartAsync(TestPlan());
    Equal(RecordingSessionState.Recording, session.State);

    // Simulate async capture fault
    coordinator.RaiseFault("Simulated microphone fault");

    // Give the event handler time to process
    await Task.Delay(50);

    // Session should transition to Failed
    Equal(RecordingSessionState.Failed, session.State);
    if (!session.LastError?.Contains("microphone") ?? true)
    {
        throw new InvalidOperationException($"Expected fault message, got: {session.LastError}");
    }
}

static CapturePlan TestPlan() => new("screen", "system", "microphone");

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new InvalidOperationException("Sequences differ.");
    }
}

static async Task ThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

file sealed class FakeCaptureCoordinator(bool failOnStart = false, bool failOnStop = false) : ICaptureCoordinator
{
    private bool _isRunning;

#pragma warning disable CS0067
    public event EventHandler<CaptureErrorEventArgs>? CaptureFailed;
#pragma warning restore CS0067

    public CapturePlan? LastPlan { get; private set; }

    public Task StartAsync(CapturePlan plan, CancellationToken cancellationToken = default)
    {
        if (failOnStart)
        {
            throw new IOException("Simulated start failure.");
        }

        LastPlan = plan;
        _isRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            throw new InvalidOperationException("Not running.");
        }

        if (failOnStop)
        {
            throw new IOException("Simulated device loss.");
        }

        _isRunning = false;
        return Task.CompletedTask;
    }
}

file sealed class TestFailingCaptureCoordinator(TimeSpan? stopDelay = null) : ICaptureCoordinator
{
    private bool _isRunning;
    public int StopCallCount { get; private set; }
    public int StopCount { get; private set; }

    public event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

    public Task StartAsync(CapturePlan plan, CancellationToken cancellationToken = default)
    {
        _isRunning = true;
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCallCount++;
        if (stopDelay is not null) await Task.Delay(stopDelay.Value, cancellationToken);
        if (!_isRunning) return;
        _isRunning = false;
        StopCount++;
    }

    public void RaiseFault(string errorMessage)
    {
        if (_isRunning)
        {
            CaptureFailed?.Invoke(this, new(errorMessage));
        }
    }
}
