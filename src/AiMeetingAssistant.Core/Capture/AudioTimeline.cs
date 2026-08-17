namespace AiMeetingAssistant.Core.Capture;

public static class AudioTimeline
{
    public static long GetTargetFrameBeforePacket(long currentFrame, long elapsedFrames, uint packetFrames) =>
        Math.Max(currentFrame, elapsedFrames - packetFrames);

    public static long GetMissingFrames(long currentFrame, long targetFrame) =>
        Math.Max(0, targetFrame - currentFrame);
}
