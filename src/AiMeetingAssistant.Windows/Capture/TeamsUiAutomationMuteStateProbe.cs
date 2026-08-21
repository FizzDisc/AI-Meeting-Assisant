using System.Diagnostics;
using System.Windows.Automation;
using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Windows.Capture;

public sealed class TeamsUiAutomationMuteStateProbe : ITeamsMuteStateProbe
{
    private const string MicrophoneButtonAutomationId = "microphone-button";
    private AutomationElement? _cachedButton;

    public Task<TeamsMuteSnapshot> ProbeAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Probe(cancellationToken), cancellationToken);

    private TeamsMuteSnapshot Probe(CancellationToken cancellationToken)
    {
        if (_cachedButton is not null)
        {
            try
            {
                if (!_cachedButton.Current.IsOffscreen)
                {
                    var cachedName = _cachedButton.Current.Name;
                    return new(TeamsMuteLabelInterpreter.Interpret(cachedName), cachedName);
                }
            }
            catch (ElementNotAvailableException) { }
            _cachedButton = null;
        }
        var processIds = Process.GetProcessesByName("ms-teams").Select(process => process.Id).ToHashSet();
        if (processIds.Count == 0) return new(TeamsMuteState.NotDetected);
        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition);
        var buttonCondition = new PropertyCondition(AutomationElement.AutomationIdProperty, MicrophoneButtonAutomationId);
        foreach (AutomationElement window in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!processIds.Contains(window.Current.ProcessId)) continue;
            AutomationElement? button;
            try { button = window.FindFirst(TreeScope.Descendants, buttonCondition); }
            catch (ElementNotAvailableException) { continue; }
            if (button is null) continue;
            _cachedButton = button;
            var name = button.Current.Name;
            return new(TeamsMuteLabelInterpreter.Interpret(name), name);
        }
        _cachedButton = null;
        return new(TeamsMuteState.NotDetected);
    }
}
