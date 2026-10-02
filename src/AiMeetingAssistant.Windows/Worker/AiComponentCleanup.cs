namespace AiMeetingAssistant.Windows.Worker;

public static class AiComponentCleanup
{
    public static IReadOnlyList<string> Remove(string applicationRoot, bool models, bool runtime, string recordingsDirectory)
    {
        var errors = new List<string>();
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationRoot));
        if (!Directory.Exists(root)) return errors;
        try
        {
            EnsureNoLinkedParents(root);
            using var installLock = new FileStream(Path.Combine(root, ".runtime-install.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            var names = new List<string>();
            if (models) names.Add("models");
            if (runtime) names.AddRange(["runtime", "runtimes"]);
            var recordingRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(recordingsDirectory));
            foreach (var name in names)
            {
                var target = Path.GetFullPath(Path.Combine(root, name));
                try
                {
                    if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("The folder is outside the application's data directory.");
                    if (Overlaps(target, recordingRoot))
                        throw new IOException("This folder overlaps the recording location and was kept to protect recordings.");
                    if (!Directory.Exists(target)) continue;
                    // Preflight the entire tree before deleting any file in this component.
                    InspectTree(target);
                    DeleteTree(target);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { errors.Add($"{target}: {error.Message}"); }
            }
            if (runtime && errors.Count == 0)
            {
                var pointer = Path.Combine(root, "active-runtime.txt");
                if (File.Exists(pointer)) File.Delete(pointer);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { errors.Add(error.Message); }
        return errors;
    }

    private static bool Overlaps(string first, string second) =>
        first.Equals(second, StringComparison.OrdinalIgnoreCase)
        || first.StartsWith(second + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || second.StartsWith(first + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void EnsureNoLinkedParents(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked folders are kept. Remove them manually if no longer needed.");
    }

    private static void InspectTree(string path)
    {
        EnsureNoLinkedParents(path);
        foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("This component contains linked files or folders and was kept.");
            if ((entry.Attributes & FileAttributes.Directory) != 0) InspectTree(entry.FullName);
        }
    }

    private static void DeleteTree(string path)
    {
        EnsureNoLinkedParents(path);
        foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            EnsureNoLinkedParents(path);
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A linked file or folder appeared during cleanup; cleanup was stopped.");
            if ((entry.Attributes & FileAttributes.Directory) != 0) DeleteTree(entry.FullName);
            else entry.Delete();
        }
        Directory.Delete(path, false);
    }
}
