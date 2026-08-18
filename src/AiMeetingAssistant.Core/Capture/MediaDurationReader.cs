using System.Buffers.Binary;
using System.Text;

namespace AiMeetingAssistant.Core.Capture;

public static class MediaDurationReader
{
    public static double ReadMilliseconds(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".wav" => ReadWavMilliseconds(path),
        ".mp4" => ReadMp4Milliseconds(path),
        _ => throw new NotSupportedException($"Unsupported media type: {Path.GetExtension(path)}")
    };

    public static double ReadWavMilliseconds(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("WAV is missing RIFF signature.");
        _ = reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("WAV is missing WAVE signature.");
        uint? byteRate = null;
        uint? dataSize = null;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadUInt32();
            var contentStart = stream.Position;
            if (id == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("WAV fmt chunk is too small.");
                _ = reader.ReadUInt16(); _ = reader.ReadUInt16(); _ = reader.ReadUInt32();
                byteRate = reader.ReadUInt32();
            }
            else if (id == "data") dataSize = size;
            stream.Position = Math.Min(stream.Length, contentStart + size + (size & 1));
            if (byteRate is > 0 && dataSize is not null) return dataSize.Value * 1000d / byteRate.Value;
        }
        throw new InvalidDataException("WAV is missing fmt or data chunk.");
    }

    public static double ReadMp4Milliseconds(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadMp4Region(stream, 0, stream.Length) ?? throw new InvalidDataException("MP4 is missing a valid mvhd duration.");
    }

    private static double? ReadMp4Region(Stream stream, long start, long end)
    {
        var header = new byte[16];
        for (var position = start; position + 8 <= end;)
        {
            stream.Position = position;
            stream.ReadExactly(header.AsSpan(0, 8));
            var size32 = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
            var type = Encoding.ASCII.GetString(header, 4, 4);
            long headerSize = 8;
            long size = size32;
            if (size32 == 1) { stream.ReadExactly(header.AsSpan(8, 8)); size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8, 8)); headerSize = 16; }
            else if (size32 == 0) size = end - position;
            if (size < headerSize || position + size > end) throw new InvalidDataException($"Invalid MP4 atom {type}.");
            var contentStart = position + headerSize;
            if (type == "mvhd") return ReadMovieHeader(stream, contentStart, size - headerSize);
            if (type == "moov") { var duration = ReadMp4Region(stream, contentStart, position + size); if (duration is not null) return duration; }
            position += size;
        }
        return null;
    }

    private static double ReadMovieHeader(Stream stream, long start, long length)
    {
        if (length < 20) throw new InvalidDataException("MP4 mvhd atom is too small.");
        stream.Position = start;
        var version = stream.ReadByte();
        stream.Position = start + (version == 1 ? 20 : 12);
        Span<byte> buffer = stackalloc byte[8];
        stream.ReadExactly(buffer[..4]);
        var timeScale = BinaryPrimitives.ReadUInt32BigEndian(buffer[..4]);
        if (timeScale == 0) throw new InvalidDataException("MP4 mvhd timescale is zero.");
        if (version == 1) { stream.ReadExactly(buffer); return BinaryPrimitives.ReadUInt64BigEndian(buffer) * 1000d / timeScale; }
        stream.ReadExactly(buffer[..4]);
        return BinaryPrimitives.ReadUInt32BigEndian(buffer[..4]) * 1000d / timeScale;
    }
}
