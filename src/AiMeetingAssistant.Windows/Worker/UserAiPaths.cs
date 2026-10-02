namespace AiMeetingAssistant.Windows.Worker;

public static class UserAiPaths
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI Meeting Assistant");
    public static string Models => Path.Combine(Root, "models");
    public static string Runtime => Path.Combine(Root, "runtime");
    public static string? BundledRuntime
    {
        get
        {
            try
            {
                var pointer = Path.Combine(Root, "active-runtime.txt");
                if (!File.Exists(pointer)) return null;
                var id = File.ReadAllText(pointer).Trim();
                if (id.Length != 64 || !id.All(Uri.IsHexDigit)) return null;
                var directory = Path.Combine(Root, "runtimes", id);
                return File.Exists(Path.Combine(directory, "python.exe")) ? directory : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
    public static string Python => BundledRuntime is { } bundled ? Path.Combine(bundled, "python.exe") : Path.Combine(Runtime, "Scripts", "python.exe");
    public static string Tools => BundledRuntime ?? Path.Combine(Runtime, "Scripts");
    public static string Ffmpeg => Path.Combine(Tools, "ffmpeg.exe");

    public static void ConfigureProcess(System.Diagnostics.ProcessStartInfo start)
    {
        start.Environment.Remove("PYTHONHOME");
        start.Environment.Remove("PYTHONPATH");
        start.Environment["PYTHONNOUSERSITE"] = "1";
        start.Environment.TryGetValue("PATH", out var currentPath);
        start.Environment["PATH"] = Tools + Path.PathSeparator + currentPath;
    }
}
