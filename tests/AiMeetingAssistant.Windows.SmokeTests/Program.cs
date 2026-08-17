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

