using System.IO;
using System.Text.Json;

namespace MoaView.Services;

internal static class EnumThemePreferenceStore
{
    public static TTheme Load<TTheme>(string path, TTheme defaultTheme) where TTheme : struct, Enum
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 4096) return defaultTheme;
            using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 4 });
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("theme", out var value) &&
                value.ValueKind == JsonValueKind.String && Enum.TryParse<TTheme>(value.GetString(), out var theme) && Enum.IsDefined(theme))
                return theme;
        }
        catch (Exception ex) when (IsStorageError(ex) || ex is JsonException) { }
        return defaultTheme;
    }

    public static bool TrySave<TTheme>(string path, TTheme theme) where TTheme : struct, Enum
    {
        if (!Enum.IsDefined(theme)) return false;
        string? temporary = null;
        try
        {
            var destination = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, new { theme = theme.ToString() });
            File.Move(temporary, destination, overwrite: true);
            return true;
        }
        catch (Exception ex) when (IsStorageError(ex)) { return false; }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (IsStorageError(ex)) { }
            }
        }
    }

    private static bool IsStorageError(Exception ex) => ex is IOException or UnauthorizedAccessException or
        ArgumentException or NotSupportedException or System.Security.SecurityException;
}
