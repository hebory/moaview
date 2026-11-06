using System.Windows;
using System.Windows.Media;

namespace MoaView.Models;

internal sealed record AppPalette(bool IsDark, Color Window, Color Surface, Color Preview, Color Panel,
    Color Control, Color Text, Color Muted, Color Border, Color Accent, Color AccentText)
{
    private static readonly IReadOnlyDictionary<AppTheme, AppPalette> Palettes = new Dictionary<AppTheme, AppPalette>
    {
        [AppTheme.Dark] = Create(true, window: "#191B27", surface: "#202334", preview: "#151824", panel: "#272B40",
            control: "#30364C", text: "#F0F2FC", muted: "#BCC4D8", border: "#64718D", accent: "#4A58B3", accentText: "#B8C7FF"),
        [AppTheme.Light] = Create(false, window: "#F3F5FA", surface: "#FCFCFF", preview: "#EDF1F8", panel: "#FFFFFF",
            control: "#F0F3FA", text: "#222C42", muted: "#52627A", border: "#8393AE", accent: "#3855B4", accentText: "#2C4595"),
        [AppTheme.Sepia] = Create(false, window: "#F4EBDD", surface: "#FFF7EC", preview: "#EFE0C9", panel: "#FFF9F0",
            control: "#F3E4D0", text: "#3B2A24", muted: "#6F5141", border: "#A1876F", accent: "#8A432E", accentText: "#753B2B"),
        [AppTheme.Ocean] = Create(true, window: "#101F30", surface: "#172A42", preview: "#0C1929", panel: "#1C3550",
            control: "#21425F", text: "#E6F5FF", muted: "#B3CCD9", border: "#527998", accent: "#126685", accentText: "#8FE3F6"),
        [AppTheme.Forest] = Create(false, window: "#ECF4EA", surface: "#F7FBF2", preview: "#E5F0E4", panel: "#F9FFF8",
            control: "#E5F1E4", text: "#213727", muted: "#46634C", border: "#749477", accent: "#286245", accentText: "#205D3B"),
    };

    public static AppPalette For(AppTheme theme) => Palettes.TryGetValue(theme, out var palette) ? palette : Palettes[AppTheme.Dark];

    public ResourceDictionary CreateResources() => new()
    {
        ["AppWindowBrush"] = Brush(Window),
        ["AppSurfaceBrush"] = Brush(Surface),
        ["AppPreviewBrush"] = Brush(Preview),
        ["AppPanelBrush"] = Brush(Panel),
        ["AppControlBrush"] = Brush(Control),
        ["AppHoverBrush"] = Brush(Mix(Control, Text, 0.11)),
        ["AppPressedBrush"] = Brush(Mix(Control, Text, 0.19)),
        ["AppTextBrush"] = Brush(Text),
        ["AppMutedBrush"] = Brush(Muted),
        ["AppDisabledBrush"] = Brush(Mix(Control, Muted, 0.62)),
        ["AppBorderBrush"] = Brush(Border),
        ["AppAccentBrush"] = Brush(Accent),
        ["AppAccentTextBrush"] = Brush(AccentText),
        ["AppOnAccentBrush"] = Brushes.White,
        ["AppErrorSurfaceBrush"] = Brush(Parse(IsDark ? "#301E2A" : "#FFF2F4")),
        ["AppErrorBadgeBrush"] = Brush(Parse(IsDark ? "#552B3A" : "#F8DDE4")),
        ["AppErrorBorderBrush"] = Brush(Parse(IsDark ? "#A3667C" : "#B86178")),
        ["AppErrorTextBrush"] = Brush(Parse(IsDark ? "#FFC3D2" : "#792F48")),
        ["AppSuccessSurfaceBrush"] = Brush(Parse(IsDark ? "#172D29" : "#EBF8EF")),
        ["AppSuccessBadgeBrush"] = Brush(Parse(IsDark ? "#24483D" : "#CFEFDC")),
        ["AppSuccessBorderBrush"] = Brush(Parse(IsDark ? "#5C8A7B" : "#608D70")),
        ["AppSuccessTextBrush"] = Brush(Parse(IsDark ? "#B0F0CF" : "#215A39")),
        ["AppWarningBrush"] = Brush(Parse(IsDark ? "#F6D690" : "#744A0A")),
    };

    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static AppPalette Create(bool dark, string window, string surface, string preview, string panel, string control,
        string text, string muted, string border, string accent, string accentText) =>
        new(dark, Parse(window), Parse(surface), Parse(preview), Parse(panel), Parse(control),
            Parse(text), Parse(muted), Parse(border), Parse(accent), Parse(accentText));

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Color Mix(Color background, Color foreground, double amount) => Color.FromRgb(
        (byte)Math.Round(background.R + (foreground.R - background.R) * amount),
        (byte)Math.Round(background.G + (foreground.G - background.G) * amount),
        (byte)Math.Round(background.B + (foreground.B - background.B) * amount));
}
