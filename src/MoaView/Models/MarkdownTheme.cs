using System.Windows.Media;

namespace MoaView.Models;

public enum MarkdownTheme { Dark, Light, Sepia }

public sealed record MarkdownThemeOption(MarkdownTheme Theme, string Name, Brush Background,
    Brush Foreground, Brush Muted, Brush Accent, Brush CodeBackground, Brush Border)
{
    public static IReadOnlyList<MarkdownThemeOption> All { get; } = Array.AsReadOnly(new[]
    {
        Create(MarkdownTheme.Dark, "다크", "#181C23", "#E5E7EB", "#AAB4C3", "#93C5FD", "#252C37", "#343C4A"),
        Create(MarkdownTheme.Light, "라이트", "#FFFFFF", "#24292F", "#57606A", "#0969DA", "#F1F3F5", "#D0D7DE"),
        Create(MarkdownTheme.Sepia, "세피아", "#F5ECD9", "#44382B", "#74614C", "#80551D", "#EADDC5", "#C8B796"),
    });

    public static MarkdownThemeOption For(MarkdownTheme theme) => All.FirstOrDefault(t => t.Theme == theme) ?? All[0];

    private static MarkdownThemeOption Create(MarkdownTheme theme, string name, params string[] colors)
    {
        var brushes = colors.Select(color =>
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }).ToArray();
        return new(theme, name, brushes[0], brushes[1], brushes[2], brushes[3], brushes[4], brushes[5]);
    }
}
