using System.Net.Http;
using AiMeetingAssistant.Core.Updates;

namespace AiMeetingAssistant.Desktop;

internal static class AppUpdates
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false });
    private static readonly ReleaseUpdateClient Client = new(Http);
    private static Task<ReleaseUpdate?>? _pending;
    public static ReleaseUpdate? Latest { get; private set; }
    public static Version Current => typeof(AppVersion).Assembly.GetName().Version ?? new Version(0, 0, 0);
    public static event Action? Changed;

    // Called on the UI dispatcher. Concurrent startup/manual checks share one bounded request.
    public static Task<ReleaseUpdate?> CheckAsync() => _pending ??= CheckCoreAsync();

    private static async Task<ReleaseUpdate?> CheckCoreAsync()
    {
        try
        {
            Latest = await Client.CheckAsync();
            Changed?.Invoke();
            return Latest;
        }
        finally { _pending = null; }
    }
}
