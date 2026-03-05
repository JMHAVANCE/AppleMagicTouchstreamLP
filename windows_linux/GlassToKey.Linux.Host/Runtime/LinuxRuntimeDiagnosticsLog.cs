namespace GlassToKey.Linux.Runtime;

public static class LinuxRuntimeDiagnosticsLog
{
    private static readonly object Gate = new();

    public static string GetPath()
    {
        string? stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        string root = string.IsNullOrWhiteSpace(stateHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state")
            : stateHome;
        return Path.Combine(root, "GlassToKey.Linux", "runtime-diagnostics.log");
    }

    public static void Write(string source, string message)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            string path = GetPath();
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string line = $"{DateTimeOffset.UtcNow:O} [{source}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // Best effort diagnostics only.
        }
    }
}
