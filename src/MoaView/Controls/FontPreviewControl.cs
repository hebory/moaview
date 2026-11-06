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
            var heading = new TextBlock
            {
                Text = localizedName ?? face.FamilyNames.Values.FirstOrDefault() ?? "글꼴",
                FontSize = 20, Margin = new Thickness(0, 0, 0, 24),
            };
            heading.SetResourceReference(TextBlock.ForegroundProperty, "AppTextBrush");
            layout.Children.Add(heading);
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

    private static FrameworkElement? DrawSample(GlyphTypeface face, string sample)
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
        var run = new GlyphRun(face, 0, false, size, 1, glyphs, new Point(0, size), widths, null, null, null, null, null, null);
        var sampleControl = new FontGlyphSample { Run = run, Margin = new Thickness(0, 0, 0, 20) };
        sampleControl.SetResourceReference(Control.ForegroundProperty, "AppTextBrush");
        return sampleControl;
    }

    private sealed class FontGlyphSample : Control
    {
        public GlyphRun? Run { get; init; }

        protected override Size MeasureOverride(Size availableSize)
        {
            var run = Run;
            return run is null ? Size.Empty : new Size(Math.Min(availableSize.Width, run.AdvanceWidths.Sum()),
                Math.Max(32, run.FontRenderingEmSize * 1.4));
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == ForegroundProperty) InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (Run is not { } run || Foreground is not { } foreground || RenderSize.Width <= 0) return;
            var width = run.AdvanceWidths.Sum();
            var scale = width > RenderSize.Width ? RenderSize.Width / width : 1;
            drawingContext.PushTransform(new ScaleTransform(scale, scale));
            drawingContext.DrawGlyphRun(foreground, run);
            drawingContext.Pop();
        }
    }
}
