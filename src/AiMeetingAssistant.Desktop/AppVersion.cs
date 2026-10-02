namespace AiMeetingAssistant.Desktop;

public static class AppVersion
{
    public static string Display => $"v{typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";
}
