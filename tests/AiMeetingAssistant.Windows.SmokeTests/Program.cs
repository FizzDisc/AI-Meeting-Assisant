using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Core.Recording;
using AiMeetingAssistant.Windows.Capture;
using AiMeetingAssistant.Windows.Worker;
using System.Text.Json;

var failures = 0;

try
{
    var workerPath = Path.Combine(Environment.CurrentDirectory, "worker", "main.py");
    await using var worker = new PythonWorkerClient("python", workerPath);
    var firstHealth = await worker.CheckHealthAsync();
    var secondHealth = await worker.CheckHealthAsync();
    if (firstHealth.Status is not ("ready" or "setup-required") || firstHealth.WorkerVersion != "0.5.0" ||
        !firstHealth.Capabilities.Contains("transcription.jobs") || secondHealth.PythonVersion != firstHealth.PythonVersion ||
        firstHealth.Diagnostics.Packages.Count != 3 || firstHealth.Diagnostics.Compute.BatchSize < 1 ||
        !firstHealth.Diagnostics.Compute.SupportedPreferences.Contains("cpu-only") ||
        !firstHealth.Diagnostics.Compute.SupportedPreferences.Contains("intel-gpu"))
    {
        Console.Error.WriteLine("FAIL Python worker health/capability negotiation is inconsistent.");
        failures++;
    }
    else
    {
        Console.WriteLine($"PASS Python worker runtime diagnostics: worker {firstHealth.WorkerVersion}, Python {firstHealth.PythonVersion}, ML ready={firstHealth.MlReady}, compute={firstHealth.Diagnostics.Compute.Mode}.");
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FAIL Python worker supervisor: {exception.Message}");
    failures++;
}

try
{
    var originalOverride = Environment.GetEnvironmentVariable(PythonRuntimeResolver.OverrideVariable);
    try
    {
        Environment.SetEnvironmentVariable(PythonRuntimeResolver.OverrideVariable, "configured-python.exe");
        if (PythonRuntimeResolver.Resolve() != "configured-python.exe")
            throw new InvalidOperationException("Explicit Python runtime override was ignored.");
        Console.WriteLine("PASS Python runtime resolver honors the explicit override.");
    }
    finally
    {
        Environment.SetEnvironmentVariable(PythonRuntimeResolver.OverrideVariable, originalOverride);
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FAIL Python runtime resolver: {exception.Message}");
    failures++;
}

try
{
    var modelRoot = Path.Combine(Path.GetTempPath(), $"aima_model_{Guid.NewGuid():N}");
    Directory.CreateDirectory(modelRoot);
    var originalModel = Environment.GetEnvironmentVariable(LocalModelResolver.OverrideVariable);
    try
    {
        Environment.SetEnvironmentVariable(LocalModelResolver.OverrideVariable, modelRoot);
        if (LocalModelResolver.ResolveDevelopmentModel() != Path.GetFullPath(modelRoot))
            throw new InvalidOperationException("Explicit local model override was ignored.");
        Console.WriteLine("PASS Local model resolver honors the explicit installed-model override.");
    }
    finally
    {
        Environment.SetEnvironmentVariable(LocalModelResolver.OverrideVariable, originalModel);
        Directory.Delete(modelRoot, true);
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FAIL Local model resolver: {exception.Message}");
    failures++;
}

try
{
    var root = Path.Combine(Path.GetTempPath(), $"aima_catalog_{Guid.NewGuid():N}");
    var worker = Path.Combine(root, "worker", "models");
    Directory.CreateDirectory(Path.Combine(worker, "faster-whisper-small"));
    try
    {
        if (LocalModelResolver.SpeechModels.Select(model => model.Id).SequenceEqual(["tiny", "small", "medium"]) is false)
            throw new InvalidOperationException("Speech model catalog order or IDs are incorrect.");
        if (!LocalModelResolver.IsSpeechModelInstalled("small", root) || LocalModelResolver.IsSpeechModelInstalled("medium", root))
            throw new InvalidOperationException("Installed speech model detection is incorrect.");
        if (LocalModelResolver.ResolveSpeechModel("small", root) != Path.Combine(worker, "faster-whisper-small"))
            throw new InvalidOperationException("Selected catalog model did not resolve to its local directory.");
        Console.WriteLine("PASS Speech model catalog exposes tiny/small/medium and detects local installation state.");
    }
    finally { Directory.Delete(root, true); }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FAIL Speech model catalog: {exception.Message}");
    failures++;
}

try
{
    var root = Path.Combine(Path.GetTempPath(), $"aima_xpu_{Guid.NewGuid():N}");
    var runtime = Path.Combine(root, "worker", ".torch-xpu-spike");
    Directory.CreateDirectory(Path.Combine(runtime, "torch", "lib"));
    Directory.CreateDirectory(Path.Combine(runtime, "Library", "bin"));
    File.WriteAllBytes(Path.Combine(runtime, "torch", "lib", "c10_xpu.dll"), []);
    try
    {
        if (LocalModelResolver.ResolveTorchXpuRuntime(root) != runtime)
            throw new InvalidOperationException("Installed Intel XPU runtime was not resolved.");
        Console.WriteLine("PASS Intel XPU runtime resolver validates the isolated runtime.");
    }
    finally { Directory.Delete(root, true); }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FAIL Intel XPU runtime resolver: {exception.Message}");
    failures++;
}

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

    var audioSources = sources.Where(source => source.Kind is CaptureSourceKind.SystemAudio or CaptureSourceKind.Microphone).ToArray();
    var endpointSnapshots = await new WindowsAudioEndpointHealthProbe().ProbeAsync(audioSources);
    if (endpointSnapshots.Count != audioSources.Length || endpointSnapshots.Any(snapshot =>
            snapshot.PeakAmplitude is < 0 or > 1))
    {
        Console.Error.WriteLine("FAIL Windows endpoint health probe returned incomplete or invalid evidence.");
        failures++;
    }
    else
    {
        Console.WriteLine($"PASS Windows endpoint health probe inspected {endpointSnapshots.Count} audio endpoint(s).");
    }

    var teamsMute = await new TeamsUiAutomationMuteStateProbe().ProbeAsync();
    Console.WriteLine($"PASS Teams UI Automation mute probe returned {teamsMute.State} ({teamsMute.AccessibleName ?? "no active meeting control"}).");

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

    var sinkError = ScreenCaptureError.Describe("Failed to initialize video sink writer: invalid media type");
    if (!sinkError.Contains("H.264", StringComparison.OrdinalIgnoreCase) ||
        !sinkError.Contains("graphics driver", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("FAIL Video-sink error is not actionable.");
        failures++;
    }
    else
    {
        Console.WriteLine("PASS Video-sink failure produces actionable encoder guidance.");
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

        var handoverProviders = new List<(string Id, string Path, WasapiCaptureMode Mode, FakeAudioProvider Provider)>();
        var handover = new DualAudioCaptureCoordinator(dualDir, (id, path, mode) =>
        {
            var provider = new FakeAudioProvider();
            handoverProviders.Add((id, path, mode, provider));
            return provider;
        });
        await handover.StartAsync(new("screen", outputs[0].Id, microphones[0].Id));
        await handover.SwitchMicrophoneAsync("replacement-microphone");
        await handover.StopAsync();
        var system = handoverProviders.Single(item => item.Mode == WasapiCaptureMode.Loopback);
        var microphoneSegments = handoverProviders.Where(item => item.Mode == WasapiCaptureMode.Input).ToArray();
        if (microphoneSegments.Length != 2 || microphoneSegments[1].Id != "replacement-microphone" ||
            system.Provider.StartCount != 1 || system.Provider.StopCount != 1 ||
            microphoneSegments.Any(item => item.Provider.StartCount != 1 || item.Provider.StopCount != 1 || item.Provider.DisposeCount != 1) ||
            microphoneSegments.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
        {
            Console.Error.WriteLine("FAIL Microphone handover restarted system audio, reused a file, or leaked a provider.");
            failures++;
        }
        else Console.WriteLine("PASS Microphone handover replaces only the input provider and finalizes both segments.");

        var suppressionProviders = new List<(WasapiCaptureMode Mode, FakeAudioProvider Provider)>();
        var suppression = new DualAudioCaptureCoordinator(dualDir, (_, _, mode) =>
        {
            var provider = new FakeAudioProvider();
            suppressionProviders.Add((mode, provider));
            return provider;
        });
        await suppression.StartAsync(new("screen", outputs[0].Id, microphones[0].Id));
        suppression.SetMicrophoneSuppressed(true);
        await suppression.SwitchMicrophoneAsync("muted-replacement-microphone");
        var suppressedMicrophones = suppressionProviders.Where(item => item.Mode == WasapiCaptureMode.Input).ToArray();
        var suppressionSystem = suppressionProviders.Single(item => item.Mode == WasapiCaptureMode.Loopback).Provider;
        await suppression.StopAsync();
        if (!suppressedMicrophones.All(item => item.Provider.IsAudioSuppressed) || suppressionSystem.IsAudioSuppressed)
        {
            Console.Error.WriteLine("FAIL Microphone suppression affected the wrong stream or was lost during handover.");
            failures++;
        }
        else Console.WriteLine("PASS Microphone suppression preserves timeline silence across microphone handover.");

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
            var prefixLength = name.StartsWith("system_audio_", StringComparison.Ordinal)
                ? "system_audio_".Length
                : name.StartsWith("microphone_", StringComparison.Ordinal) ? "microphone_".Length : "screen_".Length;
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

            var combinedPaths = new List<string>();
            var combinedAudio = new List<FakeAudioProvider>();
            var combinedScreen = new FakeScreenProvider();
            var combined = new CombinedCaptureCoordinator(
                testDir,
                (_, path) => { combinedPaths.Add(path); return combinedScreen; },
                (_, path, _) =>
                {
                    var provider = new FakeAudioProvider();
                    combinedPaths.Add(path);
                    combinedAudio.Add(provider);
                    return provider;
                });
            await combined.StartAsync(new("screen:\\\\.\\DISPLAY1", "output", "microphone"));
            await combined.StopAsync();
            if (combinedPaths.Count != 3 || combinedPaths.Select(GetTimestamp).Distinct().Count() != 1 ||
                combinedScreen.StartCount != 1 || combinedScreen.StopCount != 1 || combinedScreen.DisposeCount != 1 ||
                combinedAudio.Any(provider => provider.StartCount != 1 || provider.StopCount != 1 || provider.DisposeCount != 1))
            {
                Console.Error.WriteLine("FAIL Combined capture did not use one timestamp and exactly-once lifecycle for all three streams.");
                failures++;
            }
            else
            {
                Console.WriteLine("PASS Combined capture uses one timestamp and exactly-once lifecycle for all three streams.");
            }

            var combinedSessionDirectory = Directory.GetDirectories(testDir, "session_*").Single();
            var processingDirectory = Path.Combine(combinedSessionDirectory, "processing");
            Directory.CreateDirectory(processingDirectory);
            var discoveredTranscript = Path.Combine(processingDirectory, "transcript.json");
            File.WriteAllText(discoveredTranscript, "{}");
            if (combined.FindLatestTranscript() != discoveredTranscript)
            {
                Console.Error.WriteLine("FAIL Latest transcript discovery did not return the completed session artifact.");
                failures++;
            }
            else
            {
                Console.WriteLine("PASS Latest transcript discovery restores the newest local artifact.");
            }
            using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(combinedSessionDirectory, "manifest.json"))))
            {
                var root = manifest.RootElement;
                var streams = root.GetProperty("Streams");
                if (root.GetProperty("Status").GetString() != "completed" || streams.GetArrayLength() != 3 ||
                    combined.LastCompletedSessionDirectory != combinedSessionDirectory ||
                    streams.EnumerateArray().Any(stream => Path.IsPathRooted(stream.GetProperty("RelativePath").GetString() ?? "")))
                {
                    Console.Error.WriteLine("FAIL Completed session manifest is missing relative metadata for all three streams.");
                    failures++;
                }
                else
                {
                    Console.WriteLine("PASS Completed session manifest contains timing metadata for all three relative artifacts.");
                }
            }

            var audioOnlyDirectory = Path.Combine(testDir, "audio-only");
            var audioOnlyScreenFactoryCalls = 0;
            var audioOnlyProviders = new List<FakeAudioProvider>();
            var audioOnly = new CombinedCaptureCoordinator(
                audioOnlyDirectory,
                (_, _) => { audioOnlyScreenFactoryCalls++; return new FakeScreenProvider(); },
                (_, _, _) => { var provider = new FakeAudioProvider(); audioOnlyProviders.Add(provider); return provider; });
            await audioOnly.StartAsync(new("", "output", "microphone"));
            await audioOnly.StopAsync();
            var audioOnlySession = Directory.GetDirectories(audioOnlyDirectory, "session_*").Single();
            using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(audioOnlySession, "manifest.json"))))
            {
                var streams = manifest.RootElement.GetProperty("Streams");
                if (audioOnlyScreenFactoryCalls != 0 || streams.GetArrayLength() != 2 ||
                    streams.EnumerateArray().Any(stream => stream.GetProperty("Kind").GetString() == "screen") ||
                    audioOnlyProviders.Any(provider => provider.StartCount != 1 || provider.StopCount != 1 || provider.DisposeCount != 1))
                {
                    Console.Error.WriteLine("FAIL Audio-only capture created or started a screen provider.");
                    failures++;
                }
                else
                {
                    Console.WriteLine("PASS Audio-only capture omits the screen provider and manifest stream.");
                }
            }

            var rollbackAudio = new List<FakeAudioProvider>();
            var rollbackScreen = new FakeScreenProvider();
            var rollback = new CombinedCaptureCoordinator(
                testDir,
                (_, _) => rollbackScreen,
                (_, _, _) =>
                {
                    var provider = new FakeAudioProvider(failOnStart: rollbackAudio.Count == 1);
                    rollbackAudio.Add(provider);
                    return provider;
                });
            try
            {
                await rollback.StartAsync(new("screen:\\\\.\\DISPLAY1", "output", "microphone"));
                Console.Error.WriteLine("FAIL Combined partial start should have thrown.");
                failures++;
            }
            catch (InvalidOperationException)
            {
                if (rollbackScreen.StopCount != 1 || rollbackScreen.DisposeCount != 1 ||
                    rollbackAudio.Count != 2 || rollbackAudio.Any(provider => provider.DisposeCount != 1))
                {
                    Console.Error.WriteLine("FAIL Combined partial start did not roll back screen and audio providers.");
                    failures++;
                }
                else
                {
                    Console.WriteLine("PASS Combined audio start failure rolls back screen and both audio providers.");
                }
            }

            var runtimeAudio = new List<FakeAudioProvider>();
            var runtimeScreen = new FakeScreenProvider();
            var runtimeCoordinator = new CombinedCaptureCoordinator(
                testDir,
                (_, _) => runtimeScreen,
                (_, _, _) =>
                {
                    var provider = new FakeAudioProvider();
                    runtimeAudio.Add(provider);
                    return provider;
                });
            var runtimeSession = new RecordingSession(runtimeCoordinator);
            await runtimeSession.StartAsync(new("screen:\\\\.\\DISPLAY1", "output", "microphone"));
            runtimeScreen.RaiseFault("display disconnected");
            await WaitForState(runtimeSession, RecordingSessionState.Failed);
            if (runtimeScreen.StopCount != 1 || runtimeScreen.DisposeCount != 1 ||
                runtimeAudio.Any(provider => provider.StopCount != 1 || provider.DisposeCount != 1))
            {
                Console.Error.WriteLine("FAIL Runtime screen failure did not stop and dispose all three streams.");
                failures++;
            }
            else
            {
                Console.WriteLine("PASS Runtime screen failure fails the session and cleans all three streams.");
            }

            var rapidScreens = new List<FakeScreenProvider>();
            var rapidCombinedAudio = new List<FakeAudioProvider>();
            var rapidCombined = new CombinedCaptureCoordinator(
                testDir,
                (_, _) => { var provider = new FakeScreenProvider(); rapidScreens.Add(provider); return provider; },
                (_, _, _) => { var provider = new FakeAudioProvider(); rapidCombinedAudio.Add(provider); return provider; });
            for (var cycle = 0; cycle < 10; cycle++)
            {
                await rapidCombined.StartAsync(new("screen:\\\\.\\DISPLAY1", "output", "microphone"));
                await rapidCombined.StopAsync();
            }
            if (rapidScreens.Count != 10 || rapidCombinedAudio.Count != 20 ||
                rapidScreens.Any(provider => provider.StopCount != 1 || provider.DisposeCount != 1) ||
                rapidCombinedAudio.Any(provider => provider.StopCount != 1 || provider.DisposeCount != 1))
            {
                Console.Error.WriteLine("FAIL Rapid combined cycles leaked or duplicated lifecycle calls.");
                failures++;
            }
            else
            {
                Console.WriteLine("PASS Ten rapid combined cycles clean up all three streams exactly once.");
            }

            var combinedStopAudio = new List<FakeAudioProvider>();
            var combinedStopScreen = new FakeScreenProvider();
            var combinedStopFailure = new CombinedCaptureCoordinator(
                testDir,
                (_, _) => combinedStopScreen,
                (_, _, _) =>
                {
                    var provider = new FakeAudioProvider(failOnStop: combinedStopAudio.Count == 1);
                    combinedStopAudio.Add(provider);
                    return provider;
                });
            await combinedStopFailure.StartAsync(new("screen:\\\\.\\DISPLAY1", "output", "microphone"));
            try
            {
                await combinedStopFailure.StopAsync();
                Console.Error.WriteLine("FAIL Combined audio stop failure should have propagated.");
                failures++;
            }
            catch (IOException)
            {
                if (combinedStopScreen.StopCount != 1 || combinedStopScreen.DisposeCount != 1 ||
                    combinedStopAudio.Any(provider => provider.DisposeCount != 1))
                {
                    Console.Error.WriteLine("FAIL Audio stop failure prevented screen finalization or provider disposal.");
                    failures++;
                }
                else
                {
                    Console.WriteLine("PASS Audio stop failure still finalizes screen and disposes all providers.");
                }
            }

            var shutdownAudio = new List<FakeAudioProvider>();
            var shutdownScreen = new FakeScreenProvider();
            var combinedShutdownCoordinator = new CombinedCaptureCoordinator(
                testDir,
                (_, _) => shutdownScreen,
                (_, _, _) => { var provider = new FakeAudioProvider(); shutdownAudio.Add(provider); return provider; });
            var combinedShutdownSession = new RecordingSession(combinedShutdownCoordinator);
            await combinedShutdownSession.StartAsync(new("screen:\\\\.\\DISPLAY1", "output", "microphone"));
            await combinedShutdownSession.ShutdownAsync();
            await combinedShutdownSession.ShutdownAsync();
            if (shutdownScreen.StopCount != 1 || shutdownScreen.DisposeCount != 1 ||
                shutdownAudio.Any(provider => provider.StopCount != 1 || provider.DisposeCount != 1))
            {
                Console.Error.WriteLine("FAIL Repeated combined shutdown did not clean all streams exactly once.");
                failures++;
            }
            else
            {
                Console.WriteLine("PASS Repeated combined shutdown cleans all streams exactly once.");
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

file sealed class FakeAudioProvider(bool failOnStart = false, bool failOnStop = false) : IAudioCaptureProvider, IAudioCaptureSuppression
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
    public bool IsAudioSuppressed { get; private set; }
    public void SetAudioSuppressed(bool suppressed) => IsAudioSuppressed = suppressed;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        StartCount++;
        if (failOnStart) throw new InvalidOperationException("Simulated provider start failure.");
        IsCapturing = true;
        CaptureStarted?.Invoke(this, new(48000, 2, 16));
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

file sealed class FakeScreenProvider(bool failOnStop = false) : IScreenCaptureProvider
{
    public event EventHandler? CaptureStarted;
    public event EventHandler<CaptureErrorEventArgs>? CaptureFaulted;
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public int DisposeCount { get; private set; }
    public bool IsCapturing { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        StartCount++;
        IsCapturing = true;
        CaptureStarted?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (IsCapturing) StopCount++;
        IsCapturing = false;
        if (failOnStop) throw new IOException("Simulated screen stop failure.");
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
