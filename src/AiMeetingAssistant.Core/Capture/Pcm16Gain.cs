namespace AiMeetingAssistant.Core.Capture;

public static class Pcm16Gain
{
    public const double Minimum = 0.0;
    public const double Maximum = 2.0;

    public static void ApplyInPlace(byte[] pcm16, double gain)
    {
        ArgumentNullException.ThrowIfNull(pcm16);
        if (pcm16.Length % 2 != 0) throw new ArgumentException("PCM16 data must contain complete samples.", nameof(pcm16));
        if (!double.IsFinite(gain) || gain < Minimum || gain > Maximum) throw new ArgumentOutOfRangeException(nameof(gain));
        if (gain == 1.0) return;
        for (var index = 0; index < pcm16.Length; index += 2)
        {
            var sample = BitConverter.ToInt16(pcm16, index);
            var scaled = (short)Math.Clamp(Math.Round(sample * gain), short.MinValue, short.MaxValue);
            pcm16[index] = (byte)scaled;
            pcm16[index + 1] = (byte)(scaled >> 8);
        }
    }
}
