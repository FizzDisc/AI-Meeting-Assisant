using System.Runtime.InteropServices;
using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Windows.Capture;

internal static class DisplaySourceEnumerator
{
    private const uint MonitorInfoPrimary = 0x00000001;

    public static IReadOnlyList<CaptureSource> Enumerate()
    {
        var monitors = new List<MonitorDescriptor>();
        MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = (uint)Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var width = info.Monitor.Right - info.Monitor.Left;
                var height = info.Monitor.Bottom - info.Monitor.Top;
                monitors.Add(new(info.DeviceName, width, height, (info.Flags & MonitorInfoPrimary) != 0));
            }

            return true;
        };

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            throw new InvalidOperationException($"Windows could not enumerate displays (error {Marshal.GetLastWin32Error()}).");
        }

        return monitors
            .OrderByDescending(monitor => monitor.IsPrimary)
            .ThenBy(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase)
            .Select((monitor, index) => new CaptureSource(
                $"screen:{monitor.DeviceName}",
                $"Display {index + 1} · {monitor.Width}×{monitor.Height}{(monitor.IsPrimary ? " · Primary" : string.Empty)}",
                CaptureSourceKind.Screen,
                true))
            .ToArray();
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, IntPtr monitorRectangle, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clipRectangle, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public uint Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    private sealed record MonitorDescriptor(string DeviceName, int Width, int Height, bool IsPrimary);
}
