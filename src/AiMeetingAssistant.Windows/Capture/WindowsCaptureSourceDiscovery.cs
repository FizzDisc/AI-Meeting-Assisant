using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Windows.Capture;

public sealed class WindowsCaptureSourceDiscovery : ICaptureSourceDiscovery
{
    public Task<IReadOnlyList<CaptureSource>> DiscoverAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<CaptureSource>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sources = new List<CaptureSource>();
            sources.AddRange(DisplaySourceEnumerator.Enumerate());
            cancellationToken.ThrowIfCancellationRequested();
            sources.AddRange(AudioEndpointEnumerator.Enumerate(AudioDataFlow.Render, CaptureSourceKind.SystemAudio));
            cancellationToken.ThrowIfCancellationRequested();
            sources.AddRange(AudioEndpointEnumerator.Enumerate(AudioDataFlow.Capture, CaptureSourceKind.Microphone));
            return sources;
        }, cancellationToken);
}

