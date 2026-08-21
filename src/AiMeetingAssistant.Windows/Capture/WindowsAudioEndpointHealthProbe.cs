using System.Runtime.InteropServices;
using AiMeetingAssistant.Core.Capture;

namespace AiMeetingAssistant.Windows.Capture;

public sealed class WindowsAudioEndpointHealthProbe : IAudioEndpointHealthProbe
{
    private static readonly Guid MeterInterface = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    private static readonly Guid VolumeInterface = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    public Task<IReadOnlyList<AudioEndpointSnapshot>> ProbeAsync(IReadOnlyList<CaptureSource> sources,
        CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<AudioEndpointSnapshot>>(() =>
    {
        var audioSources = sources.Where(source => source.Kind is CaptureSourceKind.SystemAudio or CaptureSourceKind.Microphone).ToArray();
        var enumeratorType = Type.GetTypeFromCLSID(new("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!;
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;
        try
        {
            var results = new List<AudioEndpointSnapshot>(audioSources.Length);
            foreach (var source in audioSources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IMMDevice? device = null;
                try
                {
                    Marshal.ThrowExceptionForHR(enumerator.GetDevice(NativeId(source.Id), out device));
                    results.Add(new(source.Id, source.DisplayName, source.Kind, ReadPeak(device), ReadMute(device)));
                }
                catch (COMException)
                {
                    results.Add(new(source.Id, source.DisplayName, source.Kind, null, null));
                }
                finally { Release(device); }
            }
            return results;
        }
        finally { Release(enumerator); }
    }, cancellationToken);

    private static double? ReadPeak(IMMDevice device)
    {
        object? instance = null;
        try
        {
            if (device.Activate(in MeterInterface, (uint)ComClassContext.InProcessServer, IntPtr.Zero, out instance) < 0) return null;
            var meter = (IAudioMeterInformation)instance;
            return meter.GetPeakValue(out var peak) >= 0 ? Math.Clamp(peak, 0, 1) : null;
        }
        catch (COMException) { return null; }
        finally { Release(instance); }
    }

    private static bool? ReadMute(IMMDevice device)
    {
        object? instance = null;
        try
        {
            if (device.Activate(in VolumeInterface, (uint)ComClassContext.InProcessServer, IntPtr.Zero, out instance) < 0) return null;
            var volume = (IAudioEndpointVolume)instance;
            return volume.GetMute(out var muted) >= 0 ? muted : null;
        }
        catch (COMException) { return null; }
        finally { Release(instance); }
    }

    private static string NativeId(string sourceId)
    {
        const string microphone = "microphone:";
        const string systemAudio = "system-audio:";
        if (sourceId.StartsWith(microphone, StringComparison.Ordinal)) return sourceId[microphone.Length..];
        if (sourceId.StartsWith(systemAudio, StringComparison.Ordinal)) return sourceId[systemAudio.Length..];
        return sourceId;
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}
