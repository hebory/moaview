using System.IO;
using MoaView.Models;

namespace MoaView.Services;

public interface IAppThemeSettings
{
    AppTheme Load();
    bool TrySave(AppTheme theme);
}

public sealed class FileAppThemeSettings : IAppThemeSettings
{
    private readonly string _path;

    public FileAppThemeSettings(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MoaView", "app-theme.json");

    public AppTheme Load() => EnumThemePreferenceStore.Load(_path, AppTheme.Dark);

    public bool TrySave(AppTheme theme) => EnumThemePreferenceStore.TrySave(_path, theme);
}
