using System.IO;

namespace MoaView.Services;

/// <summary>Small local lifecycle log. Never records file paths, text, or general keyboard input.</summary>
internal static class ExplorerPreviewDiagnostics
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var directory = MoaViewLocalAppData.GetPath("Logs");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "explorer-space.log");
                if (File.Exists(path) && new FileInfo(path).Length > 128 * 1024)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging is best effort, including read-only profiles.
        }
    }
}
