using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MoaView.Controls;

/// <summary>Small, font-independent vector icon for MoaView controls.</summary>
public sealed class AppIcon : Control
{
    private const double ViewBoxSize = 24;
    private static readonly IReadOnlyDictionary<AppIconKind, IconGeometry[]> Icons = CreateIcons();

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(AppIconKind), typeof(AppIcon),
        new FrameworkPropertyMetadata(AppIconKind.Image, FrameworkPropertyMetadataOptions.AffectsRender));

    public AppIconKind Kind
    {
        get => (AppIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public AppIcon()
    {
        Width = 18;
        Height = 18;
        Focusable = false;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ForegroundProperty) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (!Icons.TryGetValue(Kind, out var icon) || RenderSize.Width <= 0 || RenderSize.Height <= 0)
            return;

        var scale = Math.Min(RenderSize.Width, RenderSize.Height) / ViewBoxSize;
        var offsetX = (RenderSize.Width - ViewBoxSize * scale) / 2;
        var offsetY = (RenderSize.Height - ViewBoxSize * scale) / 2;
        drawingContext.PushTransform(new TranslateTransform(offsetX, offsetY));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));

        var brush = Foreground ?? Brushes.Black;
        var pen = new Pen(brush, 1.8)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        if (pen.CanFreeze)
            pen.Freeze();

        foreach (var item in icon)
        {
            drawingContext.DrawGeometry(item.IsFilled ? brush : null, item.IsFilled ? null : pen, item.Geometry);
        }

        drawingContext.Pop();
        drawingContext.Pop();
    }

    private static IReadOnlyDictionary<AppIconKind, IconGeometry[]> CreateIcons() => new Dictionary<AppIconKind, IconGeometry[]>
    {
        [AppIconKind.Open] = Paths("M3 7.5A2.5 2.5 0 0 1 5.5 5H10l2 2h6.5A2.5 2.5 0 0 1 21 9.5V18a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z", "M3 11h18"),
        [AppIconKind.Settings] = Paths("M12 8.7A3.3 3.3 0 1 0 12 15.3A3.3 3.3 0 1 0 12 8.7Z",
            "M9.8 4.9 10.4 2.8 13.6 2.8 14.2 4.9 15.5 5.5 17.3 4.4 19.6 6.7 18.5 8.5 19.1 9.8 21.2 10.4 21.2 13.6 19.1 14.2 18.5 15.5 19.6 17.3 17.3 19.6 15.5 18.5 14.2 19.1 13.6 21.2 10.4 21.2 9.8 19.1 8.5 18.5 6.7 19.6 4.4 17.3 5.5 15.5 4.9 14.2 2.8 13.6 2.8 10.4 4.9 9.8 5.5 8.5 4.4 6.7 6.7 4.4 8.5 5.5Z"),
        [AppIconKind.Previous] = Paths("M14.5 5 7.5 12l7 7", "M8 12h9"),
        [AppIconKind.Next] = Paths("M9.5 5 16.5 12l-7 7", "M7 12h9"),
        [AppIconKind.ZoomIn] = Paths("M10.8 10.8m-6.3 0a6.3 6.3 0 1 0 12.6 0a6.3 6.3 0 1 0-12.6 0", "M15.5 15.5 21 21", "M10.8 7.6v6.4", "M7.6 10.8H14"),
        [AppIconKind.ZoomOut] = Paths("M10.8 10.8m-6.3 0a6.3 6.3 0 1 0 12.6 0a6.3 6.3 0 1 0-12.6 0", "M15.5 15.5 21 21", "M7.6 10.8H14"),
        [AppIconKind.Fit] = Paths("M8 3H5a2 2 0 0 0-2 2v3", "M16 3h3a2 2 0 0 1 2 2v3", "M3 16v3a2 2 0 0 0 2 2h3", "M21 16v3a2 2 0 0 1-2 2h-3", "M8 8h8v8H8z"),
        [AppIconKind.More] = Paths("M5 12a1 1 0 1 0 0 .01", "M12 12a1 1 0 1 0 0 .01", "M19 12a1 1 0 1 0 0 .01"),
        [AppIconKind.Close] = Paths("m6 6 12 12", "m18 6-12 12"),
        [AppIconKind.ActualSize] = Paths("M4 4h6", "M4 4v6", "M20 4h-6", "M20 4v6", "M4 20h6", "M4 20v-6", "M20 20h-6", "M20 20v-6", "M9 12h6", "M12 9v6"),
        [AppIconKind.RotateLeft] = Paths("M4.5 10a8 8 0 1 1-.2 4", "M4 4v6h6"),
        [AppIconKind.RotateRight] = Paths("M19.5 10a8 8 0 1 0 .2 4", "M20 4v6h-6"),
        [AppIconKind.Save] = Paths("M5 3h12l4 4v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2Z", "M7 3v6h10V3", "M7 21v-8h10v8"),
        [AppIconKind.Palette] = Paths("M12 3a9 9 0 1 0 0 18h1.2a2 2 0 0 0 1.5-3.3 1.4 1.4 0 0 1 1-2.3h1.1A4.2 4.2 0 0 0 21 11.2 8.2 8.2 0 0 0 12 3Z", "M7.5 10a1 1 0 1 0 0 .01", "M10.5 7a1 1 0 1 0 0 .01", "M15 7.5a1 1 0 1 0 0 .01", "M17 11a1 1 0 1 0 0 .01"),
        [AppIconKind.Explorer] = Paths("M3 5h7l2 2h9v12H3z", "M3 10h18"),
        [AppIconKind.File] = Paths("M6 3h8l4 4v14H6z", "M14 3v5h4", "M9 12h6m-6 4h6"),
        [AppIconKind.Markdown] = Paths("M3 6a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z", "M6 15V9l2.5 3 2.5-3v6", "M14 13l2 2 2-2", "M16 15V9"),
        [AppIconKind.Help] = Paths("M12 22a10 10 0 1 0 0-20a10 10 0 1 0 0 20Z", "M9.6 9a2.5 2.5 0 1 1 4.4 1.6c-1.2 1.1-2 1.4-2 3", "M12 17.5v.01"),
        [AppIconKind.Play] = FilledPaths("M8 5.5a1 1 0 0 1 1.5-.86l10 6a1.6 1.6 0 0 1 0 2.72l-10 6A1 1 0 0 1 8 18.5Z"),
        [AppIconKind.Pause] = Paths("M7 5h3v14H7z", "M14 5h3v14h-3z"),
        [AppIconKind.Warning] = Paths("M10.3 4.3a2 2 0 0 1 3.4 0l7.7 13.4a2 2 0 0 1-1.7 3H4a2 2 0 0 1-1.7-3Z", "M12 9v4.5", "M12 17v.01"),
        [AppIconKind.Image] = Paths("M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Z", "M3 16l5-5 4 4 3-3 6 6", "M16.5 8a1.5 1.5 0 1 0 0 .01"),
        [AppIconKind.Replay] = Paths("M4.5 9.5a8 8 0 1 1-.5 6", "M4 4.5v5h5"),
        [AppIconKind.SeekBack] = Paths("M4.5 9.5a8.5 8.5 0 1 1-.5 6", "M4 4v5.5h5.5",
            "M8 12.5l1.7-1.5v6.5", "M13 12.8a1.8 1.8 0 0 1 3.6 0v2.9a1.8 1.8 0 0 1-3.6 0Z"),
        [AppIconKind.SeekForward] = Paths("M19.5 9.5a8.5 8.5 0 1 0 .5 6", "M20 4v5.5h-5.5",
            "M8 12.5l1.7-1.5v6.5", "M13 12.8a1.8 1.8 0 0 1 3.6 0v2.9a1.8 1.8 0 0 1-3.6 0Z"),
        [AppIconKind.Volume] = Paths("M3 9h4l5-4v14l-5-4H3Z", "M15.5 9a4 4 0 0 1 0 6", "M18 6a8 8 0 0 1 0 12"),
        [AppIconKind.Muted] = Paths("M3 9h4l5-4v14l-5-4H3Z", "M15.5 9l6 6m0-6-6 6"),
    };

    private static IconGeometry[] Paths(params string[] geometries) => geometries.Select(path => Parse(path)).ToArray();

    private static IconGeometry[] FilledPaths(params string[] geometries) => geometries.Select(path => Parse(path, true)).ToArray();

    private static IconGeometry Parse(string path, bool isFilled = false)
    {
        var geometry = Geometry.Parse(path);
        if (geometry.CanFreeze)
            geometry.Freeze();
        return new IconGeometry(geometry, isFilled);
    }

    private sealed record IconGeometry(Geometry Geometry, bool IsFilled);
}

public enum AppIconKind
{
    Open,
    Settings,
    Previous,
    Next,
    ZoomIn,
    ZoomOut,
    Fit,
    More,
    Close,
    ActualSize,
    RotateLeft,
    RotateRight,
    Save,
    Palette,
    Explorer,
    File,
    Markdown,
    Help,
    Play,
    Pause,
    Warning,
    Image,
    Replay,
    SeekBack,
    SeekForward,
    Volume,
    Muted
}
