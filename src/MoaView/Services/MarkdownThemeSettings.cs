using System.IO;
using System.Text.Json;
using MoaView.Models;

namespace MoaView.Services;

public interface IMarkdownThemeSettings
{
    MarkdownTheme Load();
    bool TrySave(MarkdownTheme theme);
}

public sealed class MarkdownThemeSettings : IMarkdownThemeSettings
{
    private readonly string _path;

    public MarkdownThemeSettings(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MoaView", "markdown-theme.json");

    public MarkdownTheme Load()
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 4096) return MarkdownTheme.Dark;
            using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 4 });
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("theme", out var value) &&
                value.ValueKind == JsonValueKind.String && Enum.TryParse<MarkdownTheme>(value.GetString(), out var theme) && Enum.IsDefined(theme))
                return theme;
        }
        catch (Exception ex) when (IsStorageError(ex) || ex is JsonException) { }
        return MarkdownTheme.Dark;
    }

    public bool TrySave(MarkdownTheme theme)
    {
        if (!Enum.IsDefined(theme)) return false;
        string? temporary = null;
        try
        {
            var destination = Path.GetFullPath(_path);
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
