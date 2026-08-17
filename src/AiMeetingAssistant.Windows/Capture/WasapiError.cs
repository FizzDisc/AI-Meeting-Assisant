namespace AiMeetingAssistant.Windows.Capture;

public static class WasapiError
{
    public const int DeviceInvalidated = unchecked((int)0x88890004);
    public const int DeviceInUse = unchecked((int)0x8889000A);
    public const int AccessDenied = unchecked((int)0x80070005);

    public static string Describe(string operation, int hresult) => hresult switch
    {
        DeviceInvalidated => $"{operation}: the selected audio device was disconnected or changed (HRESULT: 0x{hresult:X8}). Refresh devices and select an available endpoint.",
        DeviceInUse => $"{operation}: the selected audio device is unavailable or in exclusive use (HRESULT: 0x{hresult:X8}). Close the conflicting application or choose another endpoint.",
        AccessDenied => $"{operation}: Windows denied access to the audio device (HRESULT: 0x{hresult:X8}). Check microphone privacy permissions.",
        _ => $"{operation} (HRESULT: 0x{hresult:X8})."
    };
}
