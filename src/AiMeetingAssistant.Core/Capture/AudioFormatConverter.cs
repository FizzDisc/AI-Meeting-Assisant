namespace AiMeetingAssistant.Core.Capture;

/// <summary>
/// Converts audio samples between different formats to PCM16 for WAV output.
/// Handles PCM16, PCM24, and IEEE Float32 with deterministic conversions.
/// </summary>
public static class AudioFormatConverter
{
    public enum AudioFormat
    {
        Pcm16,
        Pcm24,
        IeeeFloat32,
        Unknown
    }

    /// <summary>
    /// Determines the audio format from sample size and format tag.
    /// </summary>
    public static AudioFormat DetermineFormat(ushort formatTag, ushort bitsPerSample)
    {
        if (formatTag == 1) // WAVE_FORMAT_PCM
        {
            return bitsPerSample == 16 ? AudioFormat.Pcm16 :
                   bitsPerSample == 24 ? AudioFormat.Pcm24 :
                   AudioFormat.Unknown;
        }
        if (formatTag == 3) // WAVE_FORMAT_IEEE_FLOAT
        {
            return bitsPerSample == 32 ? AudioFormat.IeeeFloat32 : AudioFormat.Unknown;
        }
        return AudioFormat.Unknown;
    }

    /// <summary>
    /// Converts audio buffer to PCM16 format.
    /// Throws if format is unknown/unsupported.
    /// </summary>
    public static byte[] ConvertToPcm16(byte[] sourceBuffer, AudioFormat format, int sampleCount)
    {
        ArgumentNullException.ThrowIfNull(sourceBuffer);
        if (sampleCount < 0) throw new ArgumentOutOfRangeException(nameof(sampleCount));
        var requiredBytes = format switch
        {
            AudioFormat.Pcm16 => checked(sampleCount * 2),
            AudioFormat.Pcm24 => checked(sampleCount * 3),
            AudioFormat.IeeeFloat32 => checked(sampleCount * 4),
            _ => 0
        };
        if (sourceBuffer.Length < requiredBytes)
            throw new ArgumentException("The source buffer is shorter than the requested sample count.", nameof(sourceBuffer));

        return format switch
        {
            AudioFormat.Pcm16 => sourceBuffer[..requiredBytes],
            AudioFormat.Pcm24 => ConvertPcm24ToPcm16(sourceBuffer, sampleCount),
            AudioFormat.IeeeFloat32 => ConvertFloat32ToPcm16(sourceBuffer, sampleCount),
            AudioFormat.Unknown => throw new InvalidOperationException("Unsupported audio format for conversion."),
            _ => throw new InvalidOperationException($"Unknown audio format: {format}")
        };
    }

    private static byte[] ConvertFloat32ToPcm16(byte[] floatBuffer, int sampleCount)
    {
        byte[] pcm16Buffer = new byte[sampleCount * 2];

        for (int i = 0; i < sampleCount; i++)
        {
            float sample = BitConverter.ToSingle(floatBuffer, i * 4);
            // Clamp to [-1.0, 1.0] and convert to int16
            float clamped = Math.Max(-1f, Math.Min(1f, sample));
            short pcm16 = (short)(clamped * 32767);
            BitConverter.GetBytes(pcm16).CopyTo(pcm16Buffer, i * 2);
        }

        return pcm16Buffer;
    }

    private static byte[] ConvertPcm24ToPcm16(byte[] pcm24Buffer, int sampleCount)
    {
        byte[] pcm16Buffer = new byte[sampleCount * 2];

        for (int i = 0; i < sampleCount; i++)
        {
            // Read 24-bit sample (little-endian)
            int sample24 = pcm24Buffer[i * 3] | (pcm24Buffer[i * 3 + 1] << 8) | (pcm24Buffer[i * 3 + 2] << 16);
            if ((sample24 & 0x800000) != 0)
                sample24 |= unchecked((int)0xFF000000);

            // Convert to 16-bit by shifting right 8 bits
            short pcm16 = (short)(sample24 >> 8);
            BitConverter.GetBytes(pcm16).CopyTo(pcm16Buffer, i * 2);
        }

        return pcm16Buffer;
    }
}
