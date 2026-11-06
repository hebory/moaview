using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MoaView.Services;

namespace MoaView.Controls;

internal sealed class FontPreviewControl : SpecializedPreviewControl
{
    private readonly string _path;

    public FontPreviewControl(string path)
    {
        _path = path;
        Loaded += ShowFont;
    }

    private void ShowFont(object sender, RoutedEventArgs e)
    {
        Loaded -= ShowFont;
        try
        {
            var face = new GlyphTypeface(new Uri(_path, UriKind.Absolute));
            var layout = new StackPanel { Margin = new Thickness(24), Orientation = Orientation.Vertical };
            face.FamilyNames.TryGetValue(CultureInfo.CurrentUICulture, out var localizedName);
            layout.Children.Add(new TextBlock
            {
                Text = localizedName ?? face.FamilyNames.Values.FirstOrDefault() ?? "글꼴",
                Foreground = Brushes.White, FontSize = 20, Margin = new Thickness(0, 0, 0, 24),
            });
            foreach (var sample in new[] { "ABCDEFGHIJKLMNOPQRSTUVWXYZ", "abcdefghijklmnopqrstuvwxyz", "0123456789 !?@#%&", "모아뷰에서 한글을 미리 봅니다" })
            {
                if (DrawSample(face, sample) is { } image) layout.Children.Add(image);
            }
            if (layout.Children.Count == 1)
            {
                ShowError("이 글꼴에는 예시 문구를 표시할 수 있는 문자가 없습니다.");
                return;
            }
            Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = layout });
            ShowReady();
        }
        catch (Exception ex) when (FilePreviewService.IsPreviewError(ex) || ex is FormatException)
        {
            ShowError("글꼴을 표시할 수 없습니다. 파일이 손상되었거나 Windows에서 읽을 수 없는 글꼴입니다.");
        }
    }

    private static Image? DrawSample(GlyphTypeface face, string sample)
    {
        const double size = 32;
        var glyphs = new List<ushort>();
        var widths = new List<double>();
        foreach (var character in sample)
        {
            if (!face.CharacterToGlyphMap.TryGetValue(character, out var glyph) || glyph == 0) continue;
            glyphs.Add(glyph);
            widths.Add(face.AdvanceWidths[glyph] * size);
        }
        if (glyphs.Count == 0) return null;
        var run = new GlyphRun(face, 0, false, size, 1, glyphs, new Point(), widths, null, null, null, null, null, null);
        return new Image
        {
            Source = new DrawingImage(new GlyphRunDrawing(Brushes.White, run)), Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 20),
        };
    }
}
