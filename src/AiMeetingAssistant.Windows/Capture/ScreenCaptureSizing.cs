namespace AiMeetingAssistant.Windows.Capture;

public static class ScreenCaptureSizing
{
    public const int MaximumWidth = 3840;
    public const int MaximumHeight = 2160;

    public static (int Width, int Height) FitWithinEncoderLimit(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Display dimensions must be positive.");
        var scale = Math.Min(1d, Math.Min((double)MaximumWidth / width, (double)MaximumHeight / height));
        return (MakeEven(width * scale), MakeEven(height * scale));
    }

    private static int MakeEven(double value)
    {
        return Math.Max(2, 2 * (int)Math.Round(value / 2d, MidpointRounding.AwayFromZero));
    }
}
