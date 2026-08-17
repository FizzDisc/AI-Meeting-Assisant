using System.Runtime.InteropServices;

namespace AiMeetingAssistant.Windows.Capture;

/// <summary>
/// COM interfaces for WASAPI audio capture. Covers IAudioClient, IAudioCaptureClient, and format negotiation.
/// </summary>

internal enum AudioClientShareMode
{
    Shared,
    Exclusive
}

internal enum AudioClientStreamFlags : uint
{
    CrossProcess = 0x00010000,
    Loopback = 0x00020000,
    EventCallback = 0x00040000,
    NoPersist = 0x00080000,
    RateAdjust = 0x00100000,
    SampleTypeFixed = 0x00200000,
    SessionLoggingDisabled = 0x04000000,
    StreamingDisabled = 0x08000000
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormatEx
{
    public ushort FormatTag;
    public ushort Channels;
    public uint SampleRate;
    public uint AvgBytesPerSec;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort ExtensionSize;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormatExtensible
{
    public WaveFormatEx Format;
    public ushort ValidBitsPerSample;
    public uint ChannelMask;
    public Guid SubFormat;

    public const ushort WAVE_FORMAT_EXTENSIBLE = 0xFFFE;

    public static readonly Guid GUID_PCM = new Guid(0x00000001, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);
    public static readonly Guid GUID_IEEE_FLOAT = new Guid(0x00000003, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);

    public bool IsPcm => SubFormat == GUID_PCM;
    public bool IsIeeeFloat => SubFormat == GUID_IEEE_FLOAT;
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig]
    int Initialize(
        AudioClientShareMode shareMode,
        AudioClientStreamFlags streamFlags,
        long hnsBufferDuration,
        long hnsPeriodicity,
        IntPtr pFormat,
        IntPtr audioSessionGuid);

    [PreserveSig]
    int GetBufferSize(out uint pNumBufferFrames);

    [PreserveSig]
    int GetStreamLatency(out long phnsLatency);

    [PreserveSig]
    int GetCurrentPadding(out uint pNumPaddingFrames);

    [PreserveSig]
    int IsFormatSupported(AudioClientShareMode shareMode, ref WaveFormatEx pFormat, out IntPtr ppClosestMatch);

    [PreserveSig]
    int GetMixFormat(out IntPtr ppDeviceFormat);

    [PreserveSig]
    int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);

    [PreserveSig]
    int Start();

    [PreserveSig]
    int Stop();

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int SetEventHandle(IntPtr eventHandle);

    [PreserveSig]
    int GetService(in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
}

[ComImport]
[Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig]
    int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead, out AudioClientBufferFlags pdwFlags, out long pu64DevicePosition, out long pu64QPCPosition);

    [PreserveSig]
    int ReleaseBuffer(uint numFramesRead);

    [PreserveSig]
    int GetNextPacketSize(out uint pNumFramesInNextPacket);
}

[Flags]
internal enum AudioClientBufferFlags : uint
{
    None = 0,
    Silent = 0x00000001,
    TimestampError = 0x00000002
}

internal enum ComClassContext : uint
{
    InProcessServer = 1,
    InProcessHandler = 2,
    LocalServer = 4,
    RemoteServer = 16,
    All = 23 // InProcessServer | InProcessHandler | LocalServer | RemoteServer
}

internal static class WasapiConstants
{
    public const uint WAVE_FORMAT_PCM = 1;
    public const uint WAVE_FORMAT_IEEE_FLOAT = 3;
    public static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
}

public enum WasapiCaptureMode
{
    Input,
    Loopback
}

public static class WasapiCaptureConfiguration
{
    public const uint EventCallbackFlag = 0x00040000;
    public const uint LoopbackFlag = 0x00020000;

    public static uint GetStreamFlags(WasapiCaptureMode mode) =>
        EventCallbackFlag | (mode == WasapiCaptureMode.Loopback ? LoopbackFlag : 0u);
}
