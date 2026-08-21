namespace AiMeetingAssistant.Core.Capture;

public enum TeamsMuteState { NotDetected, Muted, Unmuted, Unknown }

public sealed record TeamsMuteSnapshot(TeamsMuteState State, string? AccessibleName = null,
    string DetectionMethod = "ui-automation");

public interface ITeamsMuteStateProbe
{
    Task<TeamsMuteSnapshot> ProbeAsync(CancellationToken cancellationToken = default);
}

public static class TeamsMuteLabelInterpreter
{
    public static TeamsMuteState Interpret(string? accessibleName)
    {
        if (string.IsNullOrWhiteSpace(accessibleName)) return TeamsMuteState.Unknown;
        var value = accessibleName.Trim().ToLowerInvariant();
        // Accessibility names describe the action that clicking the button
        // would perform, hence "mute" means the microphone is currently live.
        if (value.Contains("unmute") || value.Contains("stumm aufheben")
            || value.Contains("stummschaltung aufheben") || value.Contains("mikrofon aktivieren"))
            return TeamsMuteState.Muted;
        if (value.Contains("mute") || value.Contains("stummschalten"))
            return TeamsMuteState.Unmuted;
        return TeamsMuteState.Unknown;
    }
}
