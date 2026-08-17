using AiMeetingAssistant.Core.Capture;
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
        var dualCoordinator = new RealCaptureCoordinator(dualDir, (_, path, mode) =>
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
        var partialCoordinator = new RealCaptureCoordinator(dualDir, (_, _, _) =>
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

        static string GetTimestamp(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var prefixLength = name.StartsWith("system_audio_", StringComparison.Ordinal) ? "system_audio_".Length : "microphone_".Length;
            return name.Substring(prefixLength, 19);
        }

        // Test 3: RealCaptureCoordinator initialization (not actual capture, just constructor)
        var testDir = Path.Combine(Path.GetTempPath(), "aima_smoke_test");
        Directory.CreateDirectory(testDir);

        try
        {
            var coordinator = new RealCaptureCoordinator(testDir);
            Console.WriteLine("PASS RealCaptureCoordinator initialized (constructor only).");
            // NOTE: Actual capture start/stop requires real audio device and is tested in manual smoke tests.
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

file sealed class FakeAudioProvider(bool failOnStart = false) : IAudioCaptureProvider
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
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        IsCapturing = false;
        return ValueTask.CompletedTask;
    }
}
