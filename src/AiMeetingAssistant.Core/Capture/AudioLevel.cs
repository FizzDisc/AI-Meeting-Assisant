namespace AiMeetingAssistant.Core.Capture;

/// <summary>
/// Represents the instantaneous audio level for a single capture stream.
/// </summary>
public sealed record AudioLevel(double PeakAmplitude, double RmsDb, long FrameCount)
{
    /// <summary>
    /// No audio signal.
    /// </summary>
    public static readonly AudioLevel Silent = new(0, double.NegativeInfinity, 0);
}
