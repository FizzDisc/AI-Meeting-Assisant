using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Core.Recording;
using AiMeetingAssistant.Core.Transcripts;
using AiMeetingAssistant.Core.Meetings;
using AiMeetingAssistant.Core.Status;
using AiMeetingAssistant.Core.Storage;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

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
    ,("media duration reader parses WAV and MP4", MediaDurationReaderParsesWavAndMp4)
    ,("alignment analyzer calculates stream end spread", AlignmentAnalyzerCalculatesSpread)
    ,("interrupted session recovery preserves artifacts", InterruptedSessionRecoveryPreservesArtifacts)
    ,("capture storage guard enforces required capacity", CaptureStorageGuardEnforcesCapacity)
    ,("audio timeline fills missing silent frames", AudioTimelineFillsMissingFrames)
    ,("transcript parser validates and orders source segments", TranscriptParserValidatesAndOrders)
    ,("transcript Markdown and JSON exports are atomic", TranscriptExportsAreAtomic)
    ,("speaker display names are validated and stored per meeting", SpeakerNamesAreMeetingScoped)
    ,("transcript run catalog preserves comparable model runs", TranscriptRunCatalogPreservesRuns)
    ,("meeting library discovers valid and invalid sessions", MeetingLibraryDiscoversAllSessions)
    ,("meeting library deletion is scoped to direct session workspaces", MeetingLibraryDeletionIsScoped)
    ,("operational status log is bounded and deduplicated", OperationalStatusLogIsBounded)
    ,("incremental audio chunks are finalized atomically", IncrementalAudioChunksAreFinalizedAtomically)
    ,("incremental transcripts reconcile meeting timestamps and boundary overlap", IncrementalTranscriptsReconcileTimeline)
    ,("paired incremental transcripts split into source artifacts", PairedIncrementalTranscriptsSplitBySource)
    ,("successful incremental cleanup removes only reproducible processing data", IncrementalCleanupIsSafelyScoped)
    ,("audio signal health distinguishes never-seen silence and recovery", AudioSignalHealthTracksRecovery)
    ,("audio endpoint guidance reports mute and active alternatives", AudioEndpointGuidanceIsEvidenceBased)
    ,("Teams accessibility labels map to current mute state", TeamsMuteLabelsDescribeCurrentState)
    ,("PCM16 recording gain scales and clips deterministically", Pcm16GainScalesAndClips)
    ,("storage inventory classifies session evidence", StorageInventoryClassifiesEvidence)
    ,("capture compression planning is evidence based and non destructive", CaptureCompressionPlanningIsSafe)
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

static async Task IncrementalAudioChunksAreFinalizedAtomically()
{
    var directory = Path.Combine(Path.GetTempPath(), $"aima_live_chunks_{Guid.NewGuid():N}");
    try
    {
        const int sampleRate = 100;
        await using var writer = new IncrementalAudioChunkWriter(directory, "microphone", sampleRate, 1, targetChunkSeconds: 1);
        var finalized = new List<IncrementalAudioChunkReadyEventArgs>();
        writer.ChunkFinalized += (_, chunk) => finalized.Add(chunk);
        writer.TryEnqueue(new(AudioLevel.Silent, 0, 75, new byte[75 * 2]));
        writer.TryEnqueue(new(AudioLevel.Silent, 75, 75, new byte[75 * 2]));
        writer.TryEnqueue(new(AudioLevel.Silent, 150, 100, new byte[100 * 2]));
        await writer.CompleteAsync();

        var chunkDirectory = Path.Combine(directory, "processing", "live-chunks", "microphone");
        var chunks = Directory.GetFiles(chunkDirectory, "chunk_*.wav");
        Equal(3, chunks.Length);
        Equal(244L, new FileInfo(chunks[0]).Length);
        Equal(244L, new FileInfo(chunks[1]).Length);
        Equal(144L, new FileInfo(chunks[2]).Length);
        Equal(0, Directory.GetFiles(chunkDirectory, "*.partial").Length);

        var manifest = JsonSerializer.Deserialize<IncrementalAudioChunkManifest>(
            await File.ReadAllTextAsync(Path.Combine(chunkDirectory, "chunks.json")))
            ?? throw new InvalidOperationException("Chunk manifest could not be read.");
        Equal("completed", manifest.Status);
        Equal(3, manifest.Chunks.Count);
        Equal(0L, manifest.DroppedBufferCount);
        Equal(3, finalized.Count);
        Equal("microphone", finalized[0].Source);
        Equal(Path.GetFullPath(chunks[0]), finalized[0].AudioPath);
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static Task IncrementalTranscriptsReconcileTimeline()
{
    var directory = Path.Combine(Path.GetTempPath(), $"aima_reconcile_{Guid.NewGuid():N}");
    try
    {
        foreach (var source in new[] { "microphone", "system_audio" })
        {
            var chunks = Path.Combine(directory, "processing", "live-chunks", source);
            var transcripts = Path.Combine(directory, "processing", "live-transcripts", source);
            Directory.CreateDirectory(chunks);
            Directory.CreateDirectory(transcripts);
            var manifest = new IncrementalAudioChunkManifest(1, source, "completed", 16000, 1, 30, 0, 0,
                [new(0, "chunk_000000.wav", 0, 480000, 0, 30), new(1, "chunk_000001.wav", 480000, 480000, 30, 30)]);
            File.WriteAllText(Path.Combine(chunks, "chunks.json"), JsonSerializer.Serialize(manifest));
            var firstText = source == "microphone" ? "local words" : "one two three four";
            var secondText = source == "microphone" ? "more local words" : "two three four five";
            TranscriptDocumentStore.ExportJsonAtomic(Path.Combine(transcripts, "chunk_000000.json"),
                new(3, DateTimeOffset.UtcNow, "de", new Dictionary<string, string?> { [source] = "de" }, "gpu", "fp16", 1,
                    "intel-gpu", null, [new(28, 30, firstText, source)], false, 0, "small", 10));
            TranscriptDocumentStore.ExportJsonAtomic(Path.Combine(transcripts, "chunk_000001.json"),
                new(3, DateTimeOffset.UtcNow, "de", new Dictionary<string, string?> { [source] = "de" }, "gpu", "fp16", 1,
                    "intel-gpu", null, [new(0, 2, secondText, source)], false, 0, "small", 10));
        }

        var result = IncrementalTranscriptReconciler.Reconcile(directory);
        Equal(4, result.Segments.Count);
        var finalSystem = result.Segments.Single(item => item.Source == "system_audio" && item.Start >= 30);
        Equal("five", finalSystem.Text);
        if (finalSystem.Start <= 30) throw new InvalidOperationException("Trimmed boundary timestamp was not advanced.");
        Equal("You", result.Segments.First(item => item.Source == "microphone").Speaker);
        return Task.CompletedTask;
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
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

static Task MediaDurationReaderParsesWavAndMp4()
{
    var directory = Path.Combine(Path.GetTempPath(), $"aima_duration_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var wav = Path.Combine(directory, "audio.wav");
        using (var writer = new Pcm16WavWriter(wav, 1, 48000)) writer.Write(new byte[96000]);
        Equal(1000d, MediaDurationReader.ReadMilliseconds(wav));
        var mp4 = Path.Combine(directory, "video.mp4");
        File.WriteAllBytes(mp4, CreateTestMp4(2500));
        Equal(2500d, MediaDurationReader.ReadMilliseconds(mp4));
        return Task.CompletedTask;
    }
    finally { Directory.Delete(directory, true); }
}

static Task AlignmentAnalyzerCalculatesSpread()
{
    var directory = Path.Combine(Path.GetTempPath(), $"aima_alignment_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var screen = Path.Combine(directory, "screen.mp4");
        var audio = Path.Combine(directory, "audio.wav");
        File.WriteAllBytes(screen, CreateTestMp4(1000));
        using (var writer = new Pcm16WavWriter(audio, 1, 48000)) writer.Write(new byte[96000]);
        var manifest = new CaptureSessionManifest(2, "session", "completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1200,
            TestPlan(), [new("screen", "screen.mp4", 100), new("microphone", "audio.wav", 125)]);
        var result = CaptureAlignmentAnalyzer.Analyze(directory, manifest, 50);
        Equal("aligned", result.Alignment?.Status);
        Equal(25d, result.Alignment?.EndSpreadMilliseconds);
        Equal(2, result.Streams.Count(stream => stream.MediaDurationMilliseconds == 1000));
        return Task.CompletedTask;
    }
    finally { Directory.Delete(directory, true); }
}

static Task InterruptedSessionRecoveryPreservesArtifacts()
{
    var directory = Path.Combine(Path.GetTempPath(), $"aima_recovery_{Guid.NewGuid():N}");
    var session = Path.Combine(directory, "session_test");
    Directory.CreateDirectory(session);
    try
    {
        var media = Path.Combine(session, "screen.mp4");
        File.WriteAllBytes(media, [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(session, "empty.wav"), []);
        var path = Path.Combine(session, "manifest.json");
        var manifest = new CaptureSessionManifest(2, "session_test", "recording", DateTimeOffset.UtcNow, null, null,
            TestPlan(), [new("screen", "screen.mp4", 10), new("microphone", "empty.wav", 20)]);
        CaptureSessionManifestStore.WriteAtomic(path, manifest);
        var report = CaptureSessionRecovery.RecoverInterrupted(directory);
        Equal(1, report.RecoveredSessions);
        Equal(0, report.Issues.Count);
        var recovered = JsonSerializer.Deserialize<CaptureSessionManifest>(File.ReadAllText(path))!;
        Equal("interrupted", recovered.Status);
        Equal("unavailable", recovered.Alignment?.Status);
        if (!File.Exists(media) || recovered.Alignment?.Detail?.Contains("microphone", StringComparison.Ordinal) is not true)
            throw new InvalidOperationException("Recovery did not preserve media or report missing artifacts.");
        return Task.CompletedTask;
    }
    finally { Directory.Delete(directory, true); }
}

static Task CaptureStorageGuardEnforcesCapacity()
{
    if (!CaptureStorageGuard.HasRequiredSpace(200, 200) || CaptureStorageGuard.HasRequiredSpace(199, 200))
        throw new InvalidOperationException("Storage threshold comparison is incorrect.");
    return Task.CompletedTask;
}

static byte[] CreateTestMp4(uint durationMilliseconds)
{
    var mvhdContent = new byte[20];
    BinaryPrimitives.WriteUInt32BigEndian(mvhdContent.AsSpan(12, 4), 1000);
    BinaryPrimitives.WriteUInt32BigEndian(mvhdContent.AsSpan(16, 4), durationMilliseconds);
    return CreateBox("moov", CreateBox("mvhd", mvhdContent));
}

static byte[] CreateBox(string type, byte[] content)
{
    var box = new byte[8 + content.Length];
    BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);
    Encoding.ASCII.GetBytes(type, box.AsSpan(4, 4));
    content.CopyTo(box, 8);
    return box;
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

static Task TranscriptParserValidatesAndOrders()
{
    var directory = Path.Combine(Path.GetTempPath(), $"aima_transcript_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "transcript.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion = 3,
            createdAtUtc = DateTimeOffset.UtcNow,
            language = "de",
            device = "cpu",
            computeType = "int8",
            segments = new[]
            {
                new { start = 2.0, end = 3.0, text = "remote", source = "system_audio", speaker = "SPEAKER_00", speakerAssignment = "assigned", speakerOverlapRatio = 0.9 },
                new { start = 0.5, end = 1.5, text = "local", source = "microphone", speaker = "You", speakerAssignment = "known-source", speakerOverlapRatio = 1.0 }
            }
        }));
        var document = TranscriptDocumentStore.Load(path);
        Equal(2, document.Segments.Count);
        Equal("microphone", document.Segments[0].Source);
        Equal("00:00.500", TranscriptDocumentStore.FormatTimestamp(document.Segments[0].Start));
        Equal("System audio", TranscriptDocumentStore.FormatSource(document.Segments[1].Source));
        Equal("You", TranscriptDocumentStore.FormatSpeaker(document.Segments[0]));
        Equal("SPEAKER_00", TranscriptDocumentStore.FormatSpeaker(document.Segments[1]));
        return Task.CompletedTask;
    }
    finally { Directory.Delete(directory, true); }
}

static Task TranscriptExportsAreAtomic()
{
    var directory = Path.Combine(Path.GetTempPath(), $"aima_export_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var document = new TranscriptDocument(2, DateTimeOffset.UtcNow, "de",
            new Dictionary<string, string?> { ["microphone"] = "de" }, "cpu", "int8", 2,
            "automatic", "CPU fallback", [new(1.25, 2.5, "Hallo Welt", "microphone")]);
        var markdownPath = Path.Combine(directory, "transcript.md");
        var jsonPath = Path.Combine(directory, "transcript-export.json");
        TranscriptDocumentStore.ExportMarkdownAtomic(markdownPath, document);
        TranscriptDocumentStore.ExportJsonAtomic(jsonPath, document);
        var markdown = File.ReadAllText(markdownPath);
        if (!markdown.Contains("00:01.250") || !markdown.Contains("Microphone") || !markdown.Contains("Hallo Welt"))
            throw new InvalidOperationException("Markdown export is missing timestamp, source or text.");
        Equal(1, TranscriptDocumentStore.Load(jsonPath).Segments.Count);
        if (Directory.GetFiles(directory, "*.tmp").Length != 0)
            throw new InvalidOperationException("Atomic transcript export left temporary files behind.");
        return Task.CompletedTask;
    }
    finally { Directory.Delete(directory, true); }
}

static Task PairedIncrementalTranscriptsSplitBySource()
{
    var directory = Path.Combine(Path.GetTempPath(), $"aima_batch_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var microphone = Path.Combine(directory, "microphone.json");
        var systemAudio = Path.Combine(directory, "system_audio.json");
        var combined = new TranscriptDocument(3, DateTimeOffset.UtcNow, null,
            new Dictionary<string, string?> { ["microphone"] = "de", ["system_audio"] = "en" },
            "gpu", "fp16", 1, "intel-gpu", null,
            [new(0, 1, "Hallo", "microphone"), new(0, 1, "Hello", "system_audio")],
            ModelId: "small", ProcessingDurationMilliseconds: 1200);
        IncrementalTranscriptBatchSplitter.Split(combined,
            new Dictionary<string, string> { ["microphone"] = microphone, ["system_audio"] = systemAudio });
        var local = TranscriptDocumentStore.Load(microphone);
        var remote = TranscriptDocumentStore.Load(systemAudio);
        Equal("de", local.Language);
        Equal("en", remote.Language);
        Equal("Hallo", local.Segments.Single().Text);
        Equal("Hello", remote.Segments.Single().Text);
        Equal(600L, local.ProcessingDurationMilliseconds);
        return Task.CompletedTask;
    }
    finally { Directory.Delete(directory, true); }
}

static Task IncrementalCleanupIsSafelyScoped()
{
    var root = Path.Combine(Path.GetTempPath(), $"aima_cleanup_{Guid.NewGuid():N}");
    var session = Path.Combine(root, "session_test");
    var processing = Path.Combine(session, "processing");
    Directory.CreateDirectory(Path.Combine(processing, "live-chunks", "microphone"));
    Directory.CreateDirectory(Path.Combine(processing, "live-transcripts", ".batches"));
    Directory.CreateDirectory(Path.Combine(processing, "live-transcripts", "microphone"));
    Directory.CreateDirectory(Path.Combine(processing, "preliminary"));
    try
    {
        File.WriteAllBytes(Path.Combine(session, "microphone_master.wav"), new byte[13]);
        File.WriteAllBytes(Path.Combine(processing, "live-chunks", "microphone", "chunk.wav"), new byte[17]);
        File.WriteAllText(Path.Combine(processing, "live-transcripts", ".batches", "batch.json"), "{}");
        var chunkTranscript = Path.Combine(processing, "live-transcripts", "microphone", "chunk_000000.json");
        File.WriteAllText(chunkTranscript, "{}");
        File.WriteAllBytes(Path.Combine(processing, "live-transcripts", "microphone", "normalized_microphone.wav"), new byte[19]);
        File.WriteAllText(Path.Combine(processing, "preliminary", "transcript.json"), "{}");
        File.WriteAllBytes(Path.Combine(processing, "normalized_incremental_system_audio.wav"), new byte[23]);
        File.WriteAllText(Path.Combine(processing, "transcript.json"), "{}");
        File.WriteAllText(Path.Combine(processing, "incremental-transcript-merged.json"), "{}");

        var preview = IncrementalProcessingCleanup.PreviewLibrary(root);
        if(preview.Files<5||preview.ReclaimableBytes<59||preview.Sessions!=1)throw new InvalidOperationException("Cleanup preview does not match eligible temporary evidence.");
        var result = IncrementalProcessingCleanup.CleanLibrary(root);
        if (result.DeletedFiles < 5 || result.ReclaimedBytes < 59) throw new InvalidOperationException("Cleanup did not report reclaimed evidence.");
        if (Directory.Exists(Path.Combine(processing, "live-chunks"))) throw new InvalidOperationException("Live chunks survived successful cleanup.");
        if (!File.Exists(Path.Combine(session, "microphone_master.wav")) || !File.Exists(chunkTranscript)
            || !File.Exists(Path.Combine(processing, "transcript.json"))
            || !File.Exists(Path.Combine(processing, "incremental-transcript-merged.json")))
            throw new InvalidOperationException("Cleanup removed retained meeting evidence.");
        try
        {
            IncrementalProcessingCleanup.AfterSuccessfulFinalization(root);
            throw new InvalidOperationException("Cleanup accepted a non-session directory.");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("session_*")) { }
        return Task.CompletedTask;
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

static Task AudioSignalHealthTracksRecovery()
{
    var monitor = new AudioSignalHealthMonitor(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(45), -55);
    var start = DateTimeOffset.Parse("2026-08-20T00:00:00Z");
    Equal(AudioSignalHealthState.Inactive, monitor.Evaluate(start));
    monitor.Start(start);
    Equal(AudioSignalHealthState.Waiting, monitor.Evaluate(start.AddSeconds(29)));
    Equal(AudioSignalHealthState.NeverDetected, monitor.Evaluate(start.AddSeconds(30)));
    monitor.Observe(-40, start.AddSeconds(31));
    Equal(AudioSignalHealthState.Healthy, monitor.Evaluate(start.AddSeconds(60)));
    Equal(AudioSignalHealthState.CurrentlySilent, monitor.Evaluate(start.AddSeconds(76)));
    monitor.Observe(-35, start.AddSeconds(77));
    Equal(AudioSignalHealthState.Healthy, monitor.Evaluate(start.AddSeconds(77)));
    monitor.Stop();
    Equal(AudioSignalHealthState.Inactive, monitor.Evaluate(start.AddMinutes(5)));
    return Task.CompletedTask;
}

static Task AudioEndpointGuidanceIsEvidenceBased()
{
    var selected = new CaptureSource("microphone:selected", "Selected microphone", CaptureSourceKind.Microphone, true);
    var snapshots = new AudioEndpointSnapshot[]
    {
        new(selected.Id, selected.DisplayName, selected.Kind, 0, true),
        new("microphone:other", "USB headset", selected.Kind, 0.2, false)
    };
    if (!AudioEndpointHealthAdvisor.BuildGuidance(selected, AudioSignalHealthState.NeverDetected, snapshots)
        .Contains("muted in Windows", StringComparison.Ordinal))
        throw new InvalidOperationException("Global endpoint mute was not reported.");
    snapshots[0] = snapshots[0] with { IsGloballyMuted = false };
    if (!AudioEndpointHealthAdvisor.BuildGuidance(selected, AudioSignalHealthState.CurrentlySilent, snapshots)
        .Contains("USB headset", StringComparison.Ordinal))
        throw new InvalidOperationException("Active alternative endpoint was not suggested.");
    Equal("", AudioEndpointHealthAdvisor.BuildGuidance(selected, AudioSignalHealthState.Healthy, snapshots));
    return Task.CompletedTask;
}

static Task TeamsMuteLabelsDescribeCurrentState()
{
    Equal(TeamsMuteState.Unmuted, TeamsMuteLabelInterpreter.Interpret("Mikrofon stummschalten"));
    Equal(TeamsMuteState.Muted, TeamsMuteLabelInterpreter.Interpret("Stummschaltung aufheben"));
    Equal(TeamsMuteState.Muted, TeamsMuteLabelInterpreter.Interpret("Mikrofon wieder aktivieren"));
    Equal(TeamsMuteState.Unmuted, TeamsMuteLabelInterpreter.Interpret("Mute microphone"));
    Equal(TeamsMuteState.Muted, TeamsMuteLabelInterpreter.Interpret("Unmute"));
    Equal(TeamsMuteState.Unknown, TeamsMuteLabelInterpreter.Interpret("Audio options"));
    return Task.CompletedTask;
}

static Task StorageInventoryClassifiesEvidence()
{
    var root=Path.Combine(Path.GetTempPath(),$"aima_storage_{Guid.NewGuid():N}");var session=Path.Combine(root,"session_test");var processing=Path.Combine(session,"processing");Directory.CreateDirectory(processing);
    try{File.WriteAllBytes(Path.Combine(session,"microphone.wav"),new byte[11]);File.WriteAllBytes(Path.Combine(processing,"transcript.json"),new byte[7]);File.WriteAllBytes(Path.Combine(processing,"normalized.wav"),new byte[13]);var report=StorageInventory.Scan(root);var item=report.Sessions.Single();if(item.CaptureBytes!=11||item.TranscriptBytes!=7||item.ProcessingBytes!=13||report.LibraryBytes!=31)throw new InvalidOperationException("Storage categories are incorrect.");return Task.CompletedTask;}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
}

static Task CaptureCompressionPlanningIsSafe()
{
    var root = Path.Combine(Path.GetTempPath(), $"aima_compression_{Guid.NewGuid():N}");
    var complete = Path.Combine(root, "session_complete");
    var interrupted = Path.Combine(root, "session_interrupted");
    Directory.CreateDirectory(complete);
    Directory.CreateDirectory(interrupted);
    try
    {
        var microphone = Path.Combine(complete, "microphone.wav");
        using (var writer = new Pcm16WavWriter(microphone, 1, 48000)) writer.Write(new byte[96000]);
        File.WriteAllBytes(Path.Combine(complete, "screen.mp4"), CreateTestMp4(1000));
        var completedManifest = new CaptureSessionManifest(2, "session_complete", "completed", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, 1000, TestPlan(),
            [new("microphone", "microphone.wav", 0), new("screen", "screen.mp4", 0)]);
        CaptureSessionManifestStore.WriteAtomic(Path.Combine(complete, "manifest.json"), completedManifest);

        var interruptedAudio = Path.Combine(interrupted, "microphone.wav");
        using (var writer = new Pcm16WavWriter(interruptedAudio, 1, 48000)) writer.Write(new byte[96000]);
        var interruptedManifest = completedManifest with { SessionId = "session_interrupted", Status = "interrupted",
            Streams = [new("microphone", "microphone.wav", 0)] };
        CaptureSessionManifestStore.WriteAtomic(Path.Combine(interrupted, "manifest.json"), interruptedManifest);

        var report = CaptureCompressionPlanner.AnalyzeLibrary(root);
        Equal(1, report.CandidateCount);
        var candidate = report.Sessions.Single(session => session.SessionName == "session_complete").Candidates.Single();
        Equal("microphone.wav", candidate.SourcePath);
        if (candidate.EstimatedSavingsBytes <= 0 || candidate.EstimatedArchiveBytes >= candidate.SourceBytes)
            throw new InvalidOperationException("Compression estimate is not conservative and positive.");
        if (File.Exists(Path.Combine(complete, "microphone.flac")) || !File.Exists(microphone))
            throw new InvalidOperationException("Read-only planning modified capture evidence.");
        var protectedSession = report.Sessions.Single(session => session.SessionName == "session_interrupted");
        if (protectedSession.ManifestValidated || protectedSession.Candidates.Count != 0)
            throw new InvalidOperationException("Interrupted capture was offered for archival.");
        if (!report.RequiredVerification.Any(rule => rule.Contains("retranscription", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Archive plan omits the retranscription acceptance gate.");
        return Task.CompletedTask;
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

static Task Pcm16GainScalesAndClips()
{
    var samples = new short[] { 10000, -10000, 30000, -30000 };
    var bytes = samples.SelectMany(BitConverter.GetBytes).ToArray();
    Pcm16Gain.ApplyInPlace(bytes, 1.5);
    Equal((short)15000, BitConverter.ToInt16(bytes, 0));
    Equal((short)-15000, BitConverter.ToInt16(bytes, 2));
    Equal(short.MaxValue, BitConverter.ToInt16(bytes, 4));
    Equal(short.MinValue, BitConverter.ToInt16(bytes, 6));
    return Task.CompletedTask;
}

static Task SpeakerNamesAreMeetingScoped()
{
    var directory = Path.Combine(Path.GetTempPath(), $"aima_speakers_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var transcriptPath = Path.Combine(directory, "transcript.json");
        File.WriteAllText(transcriptPath, "{}");
        SpeakerNameStore.SaveForTranscript(transcriptPath, new Dictionary<string, string>
        {
            ["You"] = "Kevin",
            ["SPEAKER_00"] = "Anna",
            ["SPEAKER_01"] = "   "
        });
        var loaded = SpeakerNameStore.LoadForTranscript(transcriptPath);
        Equal("Kevin", loaded["You"]);
        Equal("Anna", loaded["SPEAKER_00"]);
        Equal(2, loaded.Count);
        Equal("Anna", TranscriptDocumentStore.FormatSpeaker(
            new TranscriptSegment(0, 1, "Hallo", "system_audio", "SPEAKER_00", "assigned"), loaded));
        return Task.CompletedTask;
    }
    finally { Directory.Delete(directory, true); }
}

static Task TranscriptRunCatalogPreservesRuns()
{
    var directory=Path.Combine(Path.GetTempPath(),$"aima_runs_{Guid.NewGuid():N}");var processing=Path.Combine(directory,"processing");Directory.CreateDirectory(processing);
    try
    {
        foreach(var item in new[]{new{Id="tiny",Ms=1200L,Hour=-2},new{Id="small",Ms=2400L,Hour=-1}})
        {
            var document=new TranscriptDocument(3,DateTimeOffset.UtcNow.AddHours(item.Hour),"de",null,"cpu","int8",2,"automatic",null,[new(0,1,"Text","system_audio","SPEAKER_00","assigned")],true,1,item.Id,item.Ms);
            TranscriptDocumentStore.ExportJsonAtomic(Path.Combine(processing,$"transcript_run_{item.Id}.json"),document);
        }
        File.Copy(Path.Combine(processing,"transcript_run_small.json"),Path.Combine(processing,"transcript.json"));
        var runs=TranscriptRunCatalog.Discover(directory);Equal(2,runs.Count);Equal("small",runs[0].ModelId);Equal("00:00:02",runs[0].ProcessingDurationLabel);return Task.CompletedTask;
    }
    finally{Directory.Delete(directory,true);}
}

static Task MeetingLibraryDiscoversAllSessions()
{
    var directory = Path.Combine(Path.GetTempPath(), $"aima_library_{Guid.NewGuid():N}");
    var valid = Path.Combine(directory, "session_valid");
    var invalid = Path.Combine(directory, "session_invalid");
    Directory.CreateDirectory(valid);
    Directory.CreateDirectory(invalid);
    try
    {
        File.WriteAllBytes(Path.Combine(valid, "microphone.wav"), [1]);
        File.WriteAllBytes(Path.Combine(valid, "system.wav"), [1]);
        var manifest = new CaptureSessionManifest(2, "session_valid", "completed", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, 1234, new("", "system", "microphone"),
            [new("microphone", "microphone.wav", 0), new("system_audio", "system.wav", 0)],
            new("aligned", 500, 25, null));
        CaptureSessionManifestStore.WriteAtomic(Path.Combine(valid, "manifest.json"), manifest);
        File.WriteAllText(Path.Combine(invalid, "manifest.json"), "not json");

        var result = MeetingLibrary.Discover(directory);
        Equal(2, result.Sessions.Count);
        Equal(1, result.Issues.Count);
        var validEntry = result.Sessions.Single(session => session.SessionId == "session_valid");
        Equal(true, validEntry.CanTranscribe);
        Equal("System audio · Microphone", validEntry.SourceSummary);
        Equal("invalid", result.Sessions.Single(session => session.SessionId == "session_invalid").Status);
        return Task.CompletedTask;
    }
    finally { Directory.Delete(directory, true); }
}

static Task MeetingLibraryDeletionIsScoped()
{
    var directory = Path.Combine(Path.GetTempPath(), $"aima_delete_{Guid.NewGuid():N}");
    var session = Path.Combine(directory, "session_delete");
    var outside = Path.Combine(Path.GetTempPath(), $"session_outside_{Guid.NewGuid():N}");
    Directory.CreateDirectory(session);
    Directory.CreateDirectory(outside);
    try
    {
        File.WriteAllText(Path.Combine(session, "artifact.txt"), "test");
        MeetingLibrary.DeleteSession(directory, session);
        Equal(false, Directory.Exists(session));
        var rejected = false;
        try { MeetingLibrary.DeleteSession(directory, outside); }
        catch (InvalidOperationException) { rejected = true; }
        Equal(true, rejected);
        Equal(true, Directory.Exists(outside));
        return Task.CompletedTask;
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        if (Directory.Exists(outside)) Directory.Delete(outside, true);
    }
}

static Task OperationalStatusLogIsBounded()
{
    var log = new OperationalStatusLog(3);
    log.Add("STARTUP", "one", DateTimeOffset.UnixEpoch);
    log.Add("STARTUP", "one", DateTimeOffset.UnixEpoch.AddSeconds(1));
    log.Add("INFO", "two", DateTimeOffset.UnixEpoch.AddSeconds(2));
    log.Add("AI", "three", DateTimeOffset.UnixEpoch.AddSeconds(3));
    var entries = log.Add("READY", "four", DateTimeOffset.UnixEpoch.AddSeconds(4));
    Equal(3, entries.Count);
    SequenceEqual(new[] { "four", "three", "two" }, entries.Select(entry => entry.Message));
    Equal(0, log.Clear().Count);
    return Task.CompletedTask;
}

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
