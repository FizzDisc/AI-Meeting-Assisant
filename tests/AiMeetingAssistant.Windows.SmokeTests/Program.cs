using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Core.Recording;
using AiMeetingAssistant.Windows.Capture;

var failures = 0;

// Test 1: Source Discovery
try
{
    var discovery = new WindowsCaptureSourceDiscovery();
    var sources = await discovery.DiscoverAsync();

    foreach (var group in sources.GroupBy(source => source.Kind))
    {
        Console.WriteLine($"{group.Key} ({group.Count()}):");
        foreach (var source in group)
        {
            Console.WriteLine($"  {source.DisplayName}");
        }
    }

    var duplicateIds = sources.GroupBy(source => source.Id).Where(group => group.Count() > 1).ToArray();
    if (duplicateIds.Length > 0)
    {
        Console.Error.WriteLine("FAIL Device identifiers must be unique.");
        failures++;
    }
    else if (sources.All(source => source.Kind is not CaptureSourceKind.Screen))
    {
        Console.Error.WriteLine("FAIL Windows returned no active display.");
        failures++;
    }
    else
    {
        Console.WriteLine("PASS Source discovery returned unique identifiers and at least one display.");
    }

    var ultraWide = ScreenCaptureSizing.FitWithinEncoderLimit(5160, 2160);
    if (ultraWide.Width != 3840 || ultraWide.Height != 1608)
    {
        Console.Error.WriteLine($"FAIL Ultra-wide screen was scaled to {ultraWide.Width}x{ultraWide.Height} instead of 3840x1608.");
        failures++;
    }
    else
    {
        Console.WriteLine("PASS 5160x2160 display is scaled proportionally to an encoder-safe 3840x1608.");
    }

    // Test 2: Microphone availability (Sprint 1.3)
    var microphones = sources.Where(s => s.Kind == CaptureSourceKind.Microphone).ToArray();
    if (microphones.Length == 0)
    {
        Console.Error.WriteLine("WARN No microphone devices found; skipping capture tests.");
    }
    else
    {
        Console.WriteLine($"PASS Found {microphones.Length} microphone(s).");

        var outputs = sources.Where(s => s.Kind == CaptureSourceKind.SystemAudio).ToArray();
        if (outputs.Length == 0)
        {
            Console.Error.WriteLine("FAIL No system audio output found for Sprint 1.4.1.");
            failures++;
        }
        else
        {
            Console.WriteLine($"PASS Found {outputs.Length} loopback source(s).");
        }

        var loopbackFlags = WasapiCaptureConfiguration.GetStreamFlags(WasapiCaptureMode.Loopback);
        if ((loopbackFlags & WasapiCaptureConfiguration.LoopbackFlag) == 0 ||
            (loopbackFlags & WasapiCaptureConfiguration.EventCallbackFlag) == 0)
        {
            Console.Error.WriteLine("FAIL Loopback capture flags are incomplete.");
            failures++;
        }
        else
        {
            Console.WriteLine("PASS Loopback capture enables loopback and event-callback flags.");
        }

        var created = new List<(string Path, WasapiCaptureMode Mode, FakeAudioProvider Provider)>();
        var dualDir = Path.Combine(Path.GetTempPath(), $"aima_dual_{Guid.NewGuid():N}");
        var dualCoordinator = new DualAudioCaptureCoordinator(dualDir, (_, path, mode) =>
        {
            var provider = new FakeAudioProvider();
            created.Add((path, mode, provider));
            return provider;
        });
        await dualCoordinator.StartAsync(new("screen", outputs[0].Id, microphones[0].Id));
        await dualCoordinator.StopAsync();
        if (created.Count != 2 || created.Select(x => x.Mode).Distinct().Count() != 2 ||
            created.Any(x => x.Provider.StartCount != 1 || x.Provider.StopCount != 1 || x.Provider.DisposeCount != 1))
        {
            Console.Error.WriteLine("FAIL Dual capture did not start and clean up both streams exactly once.");
            failures++;
        }
        else if (GetTimestamp(created[0].Path) != GetTimestamp(created[1].Path))
        {
            Console.Error.WriteLine("FAIL Dual capture filenames do not share a session timestamp.");
            failures++;
        }
        else
        {
            Console.WriteLine("PASS Dual capture uses both modes, a shared timestamp and exactly-once cleanup.");
        }

        var partialProviders = new List<FakeAudioProvider>();
        var partialCoordinator = new DualAudioCaptureCoordinator(dualDir, (_, _, _) =>
        {
            var provider = new FakeAudioProvider(failOnStart: partialProviders.Count == 1);
            partialProviders.Add(provider);
            return provider;
        });
        try
        {
            await partialCoordinator.StartAsync(new("screen", outputs[0].Id, microphones[0].Id));
            Console.Error.WriteLine("FAIL Partial start should have thrown.");
            failures++;
        }
        catch (InvalidOperationException)
        {
            if (partialProviders.Count != 2 || partialProviders.Any(p => p.DisposeCount != 1) || partialProviders[0].StopCount != 1)
            {
                Console.Error.WriteLine("FAIL Partial start did not roll back both providers exactly once.");
                failures++;
            }
            else
            {
                Console.WriteLine("PASS Microphone start failure rolls back both streams.");
            }
        }

        var rapidProviders = new List<FakeAudioProvider>();
        var rapidCoordinator = new DualAudioCaptureCoordinator(dualDir, (_, _, _) =>
        {
            var provider = new FakeAudioProvider();
            rapidProviders.Add(provider);
            return provider;
        });
        for (var cycle = 0; cycle < 10; cycle++)
        {
            await rapidCoordinator.StartAsync(new("screen", outputs[0].Id, microphones[0].Id));
            await rapidCoordinator.StopAsync();
        }
        if (rapidProviders.Count != 20 || rapidProviders.Any(p => p.StartCount != 1 || p.StopCount != 1 || p.DisposeCount != 1))
        {
            Console.Error.WriteLine("FAIL Rapid dual start/stop cycles leaked or duplicated lifecycle calls.");
            failures++;
        }
        else
        {
            Console.WriteLine("PASS Ten rapid dual start/stop cycles clean up exactly once.");
        }

        var faultProviders = new List<FakeAudioProvider>();
        var faultCoordinator = new DualAudioCaptureCoordinator(dualDir, (_, _, _) =>
        {
            var provider = new FakeAudioProvider();
            faultProviders.Add(provider);
            return provider;
        });
        var faultSession = new RecordingSession(faultCoordinator);
        await faultSession.StartAsync(new("screen", outputs[0].Id, microphones[0].Id));
        faultProviders[0].RaiseFault("device invalidated");
        faultProviders[1].RaiseFault("device invalidated again");
        await WaitForState(faultSession, RecordingSessionState.Failed);
        if (faultProviders.Any(p => p.StopCount != 1 || p.DisposeCount != 1))
        {
            Console.Error.WriteLine("FAIL Runtime device faults did not clean up both streams exactly once.");
            failures++;
        }
        else
        {
            Console.WriteLine("PASS Simultaneous runtime faults fail the session and clean up both streams once.");
        }

        var stopFailureProviders = new List<FakeAudioProvider>();
        var stopFailureCoordinator = new DualAudioCaptureCoordinator(dualDir, (_, _, _) =>
        {
            var provider = new FakeAudioProvider(failOnStop: stopFailureProviders.Count == 1);
            stopFailureProviders.Add(provider);
            return provider;
        });
        await stopFailureCoordinator.StartAsync(new("screen", outputs[0].Id, microphones[0].Id));
        try
        {
            await stopFailureCoordinator.StopAsync();
            Console.Error.WriteLine("FAIL Stop failure should have propagated.");
            failures++;
        }
        catch (IOException)
        {
            if (stopFailureProviders.Any(p => p.DisposeCount != 1))
            {
                Console.Error.WriteLine("FAIL Stop failure prevented disposal of one or more streams.");
                failures++;
            }
            else
            {
                Console.WriteLine("PASS Stop failure still disposes both streams.");
            }
        }

        var shutdownProviders = new List<FakeAudioProvider>();
        var shutdownCoordinator = new DualAudioCaptureCoordinator(dualDir, (_, _, _) =>
        {
            var provider = new FakeAudioProvider();
            shutdownProviders.Add(provider);
            return provider;
        });
        var shutdownSession = new RecordingSession(shutdownCoordinator);
        await shutdownSession.StartAsync(new("screen", outputs[0].Id, microphones[0].Id));
        await shutdownSession.ShutdownAsync();
        await shutdownSession.ShutdownAsync();
        if (shutdownProviders.Any(p => p.StopCount != 1 || p.DisposeCount != 1))
        {
            Console.Error.WriteLine("FAIL Repeated window-style shutdown did not clean up exactly once.");
            failures++;
        }
        else
        {
            Console.WriteLine("PASS Repeated window-style shutdown cleans up both streams exactly once.");
        }

        var invalidatedMessage = WasapiError.Describe("Capture failed", WasapiError.DeviceInvalidated);
        if (!invalidatedMessage.Contains("disconnected", StringComparison.OrdinalIgnoreCase) ||
            !invalidatedMessage.Contains("Refresh devices", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("FAIL Device-invalidated error is not actionable.");
            failures++;
        }
        else
        {
            Console.WriteLine("PASS Device-invalidated HRESULT produces actionable recovery guidance.");
        }

        static string GetTimestamp(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var prefixLength = name.StartsWith("system_audio_", StringComparison.Ordinal) ? "system_audio_".Length : "microphone_".Length;
            return name.Substring(prefixLength, 19);
        }

        // Test 3: Screen coordinator lifecycle with a fake provider
        var testDir = Path.Combine(Path.GetTempPath(), "aima_smoke_test");
        Directory.CreateDirectory(testDir);

        try
        {
            string? selectedScreen = null;
            string? outputPath = null;
            var fakeScreen = new FakeScreenProvider();
            var coordinator = new ScreenCaptureCoordinator(testDir, (id, path) =>
            {
                selectedScreen = id;
                outputPath = path;
                return fakeScreen;
            });
            await coordinator.StartAsync(new("screen:\\\\.\\DISPLAY1", "", ""));
            await coordinator.StopAsync();
            if (selectedScreen != "screen:\\\\.\\DISPLAY1" || outputPath is null ||
                !Path.GetFileName(outputPath).StartsWith("screen_") || Path.GetExtension(outputPath) != ".mp4" ||
                fakeScreen.StartCount != 1 || fakeScreen.StopCount != 1 || fakeScreen.DisposeCount != 1)
            {
                Console.Error.WriteLine("FAIL Screen coordinator did not use the selected display, MP4 naming and exactly-once cleanup.");
                failures++;
            }
            else
            {
                Console.WriteLine("PASS Screen coordinator uses the selected display, MP4 naming and exactly-once cleanup.");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(testDir, true);
            }
            catch { /* Ignore cleanup errors */ }
        }
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL Unexpected error: {ex.Message}");
    failures++;
}

return failures == 0 ? 0 : 1;

static async Task WaitForState(RecordingSession session, RecordingSessionState expected)
{
    var timeout = DateTime.UtcNow.AddSeconds(2);
    while (session.State != expected && DateTime.UtcNow < timeout) await Task.Delay(10);
    if (session.State != expected) throw new InvalidOperationException($"Expected {expected}, got {session.State}.");
}

file sealed class FakeAudioProvider(bool failOnStart = false, bool failOnStop = false) : IAudioCaptureProvider
{
#pragma warning disable CS0067
    public event EventHandler<AudioCaptureStartedEventArgs>? CaptureStarted;
    public event EventHandler<AudioFrameCapturedEventArgs>? FrameCaptured;
    public event EventHandler<AudioCaptureFaultEventArgs>? CaptureFaulted;
#pragma warning restore CS0067

    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public int DisposeCount { get; private set; }
    public bool IsCapturing { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        StartCount++;
        if (failOnStart) throw new InvalidOperationException("Simulated provider start failure.");
        IsCapturing = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (IsCapturing) StopCount++;
        IsCapturing = false;
        if (failOnStop) throw new IOException("Simulated stop failure.");
        return Task.CompletedTask;
    }

    public void RaiseFault(string message)
    {
        if (IsCapturing) CaptureFaulted?.Invoke(this, new(message));
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        IsCapturing = false;
        return ValueTask.CompletedTask;
    }
}

file sealed class FakeScreenProvider : IScreenCaptureProvider
{
#pragma warning disable CS0067
    public event EventHandler<CaptureErrorEventArgs>? CaptureFaulted;
#pragma warning restore CS0067
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public int DisposeCount { get; private set; }
    public bool IsCapturing { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        StartCount++;
        IsCapturing = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (IsCapturing) StopCount++;
        IsCapturing = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        IsCapturing = false;
        return ValueTask.CompletedTask;
    }
}
