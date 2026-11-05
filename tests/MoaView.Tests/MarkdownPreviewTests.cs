using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MoaView.Controls;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using MoaView.ViewModels;
using Xunit;

namespace MoaView.Tests;

public sealed class MarkdownPreviewTests
{
    private const string Sample = """
        # 미리보기 제목

        **굵게** 그리고 *기울임*과 `inline code`.

        > 인용문

        3. 첫 항목
        4. 다음 항목

        - 목록 항목

        ```csharp
        var message = "한글";
        ```

        | 항목 | 값 |
        | --- | --- |
        | 테마 | 세피아 |

        [링크](https://example.invalid) ![대체 텍스트](file:///private.png)

        <https://example.invalid/plain> A &amp; B

        <script>alert('never')</script>
        """;

    [Theory]
    [InlineData(MarkdownTheme.Dark)]
    [InlineData(MarkdownTheme.Light)]
    [InlineData(MarkdownTheme.Sepia)]
    public void ThemeSettings_RoundTripsAcrossInstances(MarkdownTheme theme)
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath("preferences/markdown-theme.json");
        var settings = new MarkdownThemeSettings(path);
        Assert.Equal(MarkdownTheme.Dark, settings.Load());
        Assert.True(settings.TrySave(theme));
        Assert.Equal(theme, new MarkdownThemeSettings(path).Load());
        Assert.False(settings.TrySave((MarkdownTheme)99));
        Assert.Equal(theme, settings.Load());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("{\"theme\":\"Unknown\"}")]
    [InlineData("{\"theme\":99}")]
    [InlineData("[]")]
    public void ThemeSettings_UsesDefaultForInvalidData(string json)
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath("settings.json");
        File.WriteAllText(path, json);
        Assert.Equal(MarkdownTheme.Dark, new MarkdownThemeSettings(path).Load());
    }

    [Fact]
    public void ThemeSettings_WriteFailureDoesNotInterruptPreviewOrLeaveTemporaryFiles()
    {
        using var temp = new TempTestDirectory();
        var destination = temp.GetPath("directory.json");
        Directory.CreateDirectory(destination);
        var path = temp.GetPath("test.md");
        File.WriteAllText(path, Sample);
        StaTestRunner.Run(() =>
        {
            var model = Model(new MarkdownThemeSettings(destination));
            Assert.Equal(PreviewStateKind.TextLoaded, model.OpenFile(path).State);
            model.SelectedMarkdownTheme = MarkdownThemeOption.For(MarkdownTheme.Light);
            Assert.Equal(MarkdownTheme.Light, model.SelectedMarkdownTheme.Theme);
            Assert.Contains("저장하지 못했습니다", model.MarkdownSettingsMessage);
            Assert.True(model.IsMarkdownRendered);
        });
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.tmp"));
    }

    [Fact]
    public void MarkdownWindow_RendersFormattingAndChangesAllThemesWithoutLosingReadingPosition()
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath("sample.MD");
        var text = Sample + string.Concat(Enumerable.Range(0, 60).Select(i => $"\n\n추가 문단 {i}"));
        File.WriteAllText(path, text);
        var settingsPath = temp.GetPath("settings.json");
        StaTestRunner.Run(() =>
        {
            var model = Model(new MarkdownThemeSettings(settingsPath));
            var window = new MainWindow(model);
            try
            {
                window.Show(); model.OpenFile(path); Render(window);
                Assert.True(window.VerifyRenderedState());
                var viewer = (MarkdownPreviewControl)window.FindName("MarkdownPreview");
                var picker = (ComboBox)window.FindName("MarkdownThemePicker");
                var document = viewer.Document;
                var title = Assert.IsType<Paragraph>(document.Blocks.FirstBlock);
                Assert.Equal(30, title.FontSize);
                Assert.DoesNotContain("#", new TextRange(title.ContentStart, title.ContentEnd).Text);
                var paragraph = document.Blocks.OfType<Paragraph>().ElementAt(1);
                Assert.Contains(paragraph.Inlines.OfType<Bold>(), b => new TextRange(b.ContentStart, b.ContentEnd).Text == "굵게");
                Assert.Contains(document.Blocks.OfType<System.Windows.Documents.List>(), list => list.StartIndex == 3);
                Assert.Single(document.Blocks.OfType<System.Windows.Documents.Table>());
                Assert.Single(document.Blocks.OfType<Section>());
                var rendered = new TextRange(document.ContentStart, document.ContentEnd).Text;
                Assert.Contains("[이미지: 대체 텍스트]", rendered);
                Assert.Contains("<script>", rendered); // HTML stays literal and never executes.
                Assert.Contains("https://example.invalid/plain", rendered);
                Assert.Contains("A & B", rendered);
                var code = document.Blocks.OfType<Paragraph>().Single(p => p.FontFamily.Source.StartsWith("Consolas"));
                var scroll = Descendant<ScrollViewer>(viewer)!;
                scroll.ScrollToVerticalOffset(200); Render(window);
                var offset = scroll.VerticalOffset;
                Assert.True(offset > 0);
                viewer.Selection.Select(title.ContentStart, title.ContentEnd);
                var selectedText = viewer.Selection.Text;
                foreach (var theme in MarkdownThemeOption.All)
                {
                    picker.SelectedItem = theme; Render(window);
                    Assert.Same(document, viewer.Document);
                    Assert.Same(theme.Background, viewer.Background);
                    Assert.Same(theme.Foreground, document.Foreground);
                    Assert.Same(theme.CodeBackground, code.Background);
                    Assert.Equal(selectedText, viewer.Selection.Text);
                    Assert.Equal(offset, scroll.VerticalOffset, 1);
                    Assert.Equal(theme.Theme, new MarkdownThemeSettings(settingsPath).Load());
                    var bitmap = new RenderTargetBitmap((int)viewer.ActualWidth, (int)viewer.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(viewer);
                    Assert.Equal(((SolidColorBrush)theme.Background).Color, SampleImageFactory.ReadPixelColor(bitmap, 5, 5));
                    var screenshots = Path.Combine(AppContext.BaseDirectory, "TestResults", "Markdown");
                    Directory.CreateDirectory(screenshots);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var screenshot = File.Create(Path.Combine(screenshots, theme.Theme + ".png"));
                    encoder.Save(screenshot);
                }
                model.ShowMarkdownSource = true; Render(window);
                var source = (TextBox)window.FindName("PreviewText");
                Assert.True(source.IsVisible);
                Assert.False(viewer.IsVisible);
                Assert.Equal(text, source.Text);
                Assert.Same(model.SelectedMarkdownTheme.Background, source.Background);
                Assert.True(window.VerifyRenderedState());
                model.ShowMarkdownSource = false; Render(window);
                Assert.True(viewer.IsVisible);
                Assert.Same(document, viewer.Document);
            }
            finally { window.Close(); }
        });
        Assert.Equal(MarkdownTheme.Sepia, new MarkdownThemeSettings(settingsPath).Load());
        Assert.Equal(text, File.ReadAllText(path));
    }

    [Fact]
    public void MarkdownWindow_ZipMarkdownAndPlainTextKeepSeparateAppearance()
    {
        using var temp = new TempTestDirectory();
        var path = ContentPreviewTests.WriteZip(temp.GetPath("docs.zip"), new Dictionary<string, byte[]>
        { ["README.md"] = Encoding.UTF8.GetBytes(Sample), ["note.txt"] = Encoding.UTF8.GetBytes("# 일반 텍스트") });
        var settings = new MarkdownThemeSettings(temp.GetPath("settings.json"));
        Assert.True(settings.TrySave(MarkdownTheme.Light));
        StaTestRunner.Run(() =>
        {
            var model = Model(settings);
            var window = new MainWindow(model);
            try
            {
                window.Show(); model.OpenFile(path);
                model.SelectedArchiveEntry = model.ArchiveEntries[0]; Render(window);
                Assert.True(model.IsMarkdownRendered);
                Assert.True(window.VerifyRenderedState());
                Assert.Equal(MarkdownTheme.Light, model.SelectedMarkdownTheme.Theme);
                Assert.False(model.CanExecuteConversion);
                model.SelectedArchiveEntry = model.ArchiveEntries[1]; Render(window);
                Assert.False(model.IsMarkdown);
                Assert.False(((FrameworkElement)window.FindName("MarkdownToolbar")).IsVisible);
                Assert.Same(MarkdownThemeOption.All[0].Background, ((TextBox)window.FindName("PreviewText")).Background);
                Assert.True(window.VerifyRenderedState());
                model.SelectedArchiveEntry = model.ArchiveEntries[0]; Render(window);
                Assert.True(model.IsMarkdownRendered);
                Assert.True(window.VerifyRenderedState());
                Assert.Same(MarkdownThemeOption.For(MarkdownTheme.Light).Background,
                    ((MarkdownPreviewControl)window.FindName("MarkdownPreview")).Background);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void MarkdownRenderer_LimitsFormattedContentAndRetainsFullSource()
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath("long.md");
        var text = new string('x', 100_100) + "END";
        File.WriteAllText(path, text);
        StaTestRunner.Run(() =>
        {
            var model = Model(new MarkdownThemeSettings(temp.GetPath("settings.json")));
            var window = new MainWindow(model);
            try
            {
                window.Show(); model.OpenFile(path); Render(window);
                var viewer = (MarkdownPreviewControl)window.FindName("MarkdownPreview");
                var rendered = new TextRange(viewer.Document.ContentStart, viewer.Document.ContentEnd).Text;
                Assert.Contains("10만 자", rendered);
                Assert.DoesNotContain("END", rendered);
                Assert.EndsWith("END", model.TextContent);
                viewer.Markdown = string.Concat(Enumerable.Repeat("*a* ", 6000));
                Assert.Contains("구조가 복잡", new TextRange(viewer.Document.ContentStart, viewer.Document.ContentEnd).Text);
            }
            finally { window.Close(); }
        });
    }

    private static MainWindowViewModel Model(IMarkdownThemeSettings settings)
    {
        var dialogs = new Win32FileDialogService();
        return new MainWindowViewModel(new FilePreviewService(), dialogs, dialogs, markdownThemeSettings: settings);
    }

    private static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (Descendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private static void Render(Window window)
    {
        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }
}
