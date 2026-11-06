namespace MoaView.Models;

public enum AppTheme
{
    Dark,
    Light,
    Sepia,
    Ocean,
    Forest
}

public sealed record AppThemeOption(AppTheme Theme, string Name)
{
    public static IReadOnlyList<AppThemeOption> All { get; } = Array.AsReadOnly(new[]
    {
        new AppThemeOption(AppTheme.Dark, "다크"),
        new AppThemeOption(AppTheme.Light, "라이트"),
        new AppThemeOption(AppTheme.Sepia, "세피아"),
        new AppThemeOption(AppTheme.Ocean, "오션"),
        new AppThemeOption(AppTheme.Forest, "포레스트")
    });
}
