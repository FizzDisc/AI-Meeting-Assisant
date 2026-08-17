namespace AiMeetingAssistant.Core.Capture;

public interface ICaptureSourceDiscovery
{
    Task<IReadOnlyList<CaptureSource>> DiscoverAsync(CancellationToken cancellationToken = default);
}

