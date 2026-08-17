using System.Runtime.InteropServices;
using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Windows.Capture;

internal static class AudioEndpointEnumerator
{
    private const uint DeviceStateActive = 0x00000001;
    private const uint StorageRead = 0;
    private static readonly PropertyKey FriendlyNameKey = new(new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public static IReadOnlyList<CaptureSource> Enumerate(AudioDataFlow dataFlow, CaptureSourceKind kind)
    {
        var enumeratorType = Type.GetTypeFromCLSID(new("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!;
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
        IMMDeviceCollection? collection = null;
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(dataFlow, DeviceStateActive, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            var defaultId = GetDefaultEndpointId(enumerator, dataFlow);
            var endpoints = new List<AudioEndpointDescriptor>((int)count);

            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(index, out device));
                    Marshal.ThrowExceptionForHR(device.GetId(out var id));
                    endpoints.Add(new(id, GetFriendlyName(device), string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)));
                }
                finally
                {
                    Release(device);
                }
            }

            var prefix = kind is CaptureSourceKind.SystemAudio ? "system-audio" : "microphone";
            return endpoints
                .OrderByDescending(endpoint => endpoint.IsDefault)
                .ThenBy(endpoint => endpoint.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(endpoint => new CaptureSource(
                    $"{prefix}:{endpoint.Id}",
                    $"{endpoint.Name}{(endpoint.IsDefault ? " · Default" : string.Empty)}",
                    kind,
                    true))
                .ToArray();
        }
        finally
        {
            Release(collection);
            Release(enumerator);
        }
    }

    private static string? GetDefaultEndpointId(IMMDeviceEnumerator enumerator, AudioDataFlow dataFlow)
    {
        IMMDevice? device = null;
        try
        {
            var result = enumerator.GetDefaultAudioEndpoint(dataFlow, AudioRole.Multimedia, out device);
            if (result < 0 || device is null)
            {
                return null;
            }

            Marshal.ThrowExceptionForHR(device.GetId(out var id));
            return id;
        }
        finally
        {
            Release(device);
        }
    }

    private static string GetFriendlyName(IMMDevice device)
    {
        IPropertyStore? propertyStore = null;
        var value = new PropVariant();
        try
        {
            Marshal.ThrowExceptionForHR(device.OpenPropertyStore(StorageRead, out propertyStore));
            Marshal.ThrowExceptionForHR(propertyStore.GetValue(in FriendlyNameKey, out value));
            return value.GetString() ?? "Unnamed audio device";
        }
        finally
        {
            value.Clear();
            Release(propertyStore);
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }

    private sealed record AudioEndpointDescriptor(string Id, string Name, bool IsDefault);
}

internal enum AudioDataFlow
{
    Render,
    Capture,
    All
}

internal enum AudioRole
{
    Console,
    Multimedia,
    Communications
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(AudioDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(AudioDataFlow dataFlow, AudioRole role, out IMMDevice device);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IntPtr client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int Item(uint index, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(in Guid interfaceId, uint classContext, IntPtr activationParameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    [PreserveSig]
    int OpenPropertyStore(uint accessMode, out IPropertyStore properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetState(out uint state);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint propertyCount);

    [PreserveSig]
    int GetAt(uint propertyIndex, out PropertyKey key);

    [PreserveSig]
    int GetValue(in PropertyKey key, out PropVariant value);

    [PreserveSig]
    int SetValue(in PropertyKey key, in PropVariant value);

    [PreserveSig]
    int Commit();
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct PropertyKey(Guid formatId, uint propertyId)
{
    private readonly Guid _formatId = formatId;
    private readonly uint _propertyId = propertyId;
}

[StructLayout(LayoutKind.Explicit)]
internal struct PropVariant
{
    [FieldOffset(0)]
    private ushort _valueType;

    [FieldOffset(8)]
    private IntPtr _pointerValue;

    public readonly string? GetString() => _valueType == 31
        ? Marshal.PtrToStringUni(_pointerValue)
        : null;

    public void Clear() => PropVariantClear(ref this);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
