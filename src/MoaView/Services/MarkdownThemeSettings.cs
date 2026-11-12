using System.IO;
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

    public MarkdownThemeSettings(string? path = null) => _path = path ?? MoaViewLocalAppData.GetPath("markdown-theme.json");

    public MarkdownTheme Load() => EnumThemePreferenceStore.Load(_path, MarkdownTheme.Dark);

    public bool TrySave(MarkdownTheme theme) => EnumThemePreferenceStore.TrySave(_path, theme);
}
