namespace AiMeetingAssistant.Core.Capture;

public sealed class Pcm16WavWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private bool _finalized;

    public Pcm16WavWriter(string path, ushort channels, uint sampleRate)
    {
        if (channels == 0) throw new ArgumentOutOfRangeException(nameof(channels));
        if (sampleRate == 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        _stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read, 65536);
        _writer = new(_stream);
        WriteHeader(channels, sampleRate);
    }

    public void Write(ReadOnlySpan<byte> pcm16Data)
    {
        ObjectDisposedException.ThrowIf(_finalized, this);
        _writer.Write(pcm16Data);
    }

    public void FinalizeFile()
    {
        if (_finalized) return;
        _finalized = true;

        _writer.Flush();
        var fileSize = _stream.Length;
        _stream.Position = 4;
        _writer.Write(checked((uint)(fileSize - 8)));
        _stream.Position = 40;
        _writer.Write(checked((uint)(fileSize - 44)));
        _writer.Flush();
        _writer.Dispose();
    }

    public void Dispose() => FinalizeFile();

    private void WriteHeader(ushort channels, uint sampleRate)
    {
        _writer.Write("RIFF"u8);
        _writer.Write(0u);
        _writer.Write("WAVE"u8);
        _writer.Write("fmt "u8);
        _writer.Write(16u);
        _writer.Write((ushort)1);
        _writer.Write(channels);
        _writer.Write(sampleRate);
        _writer.Write(checked(sampleRate * channels * 2u));
        _writer.Write(checked((ushort)(channels * 2)));
        _writer.Write((ushort)16);
        _writer.Write("data"u8);
        _writer.Write(0u);
    }
}
