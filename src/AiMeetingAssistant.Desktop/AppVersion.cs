using System.Reflection;
namespace AiMeetingAssistant.Desktop; public static class AppVersion { public static string Display => $"v{typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0"}"; }
