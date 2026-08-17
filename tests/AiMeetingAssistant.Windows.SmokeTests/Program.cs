using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Windows.Capture;

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
    return 1;
}

if (sources.All(source => source.Kind is not CaptureSourceKind.Screen))
{
    Console.Error.WriteLine("FAIL Windows returned no active display.");
    return 1;
}

Console.WriteLine("PASS Windows source discovery returned unique identifiers and at least one display.");
return 0;

