using System.IO;

namespace MoaView.Services;

/// <summary>
/// Resolves the current user's MoaView local application data directory (%LOCALAPPDATA%\MoaView),
/// honoring an explicit LOCALAPPDATA process environment override for isolated verification runs.
/// </summary>
internal static class MoaViewLocalAppData
{
    public static string GetRootDirectory()
    {
        var baseDir = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(baseDir) || !Path.IsPathRooted(baseDir))
        {
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        return Path.Combine(Path.GetFullPath(baseDir), "MoaView");
    }

    public static string GetPath(params string[] relativeSegments)
    {
        var segments = new string[relativeSegments.Length + 1];
        segments[0] = GetRootDirectory();
        Array.Copy(relativeSegments, 0, segments, 1, relativeSegments.Length);
        return Path.Combine(segments);
    }
}
