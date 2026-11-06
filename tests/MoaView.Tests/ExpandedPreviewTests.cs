using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using MoaView.ViewModels;
using Xunit;

namespace MoaView.Tests;

public sealed class ExpandedPreviewTests
{
    [Fact]
    public void UnknownFilesAndFolders_ShowBoundedTextOrBinaryDetails()
    {
        using var temp = new TempTestDirectory();
        var text = temp.GetPath("README");
        var prefix = new string('a', 512 * 1024 - 1);
        File.WriteAllText(text, prefix + "한글");
        var binary = temp.GetPath("data.unknown");
        using (var stream = File.Create(binary))
        {
            stream.Write([0x4D, 0x5A, 0x00, 0xFE]);
            stream.SetLength(32 * 1024 * 1024);
        }

        var service = new FilePreviewService();
        var textResult = service.Load(text);
        Assert.Equal("텍스트로 읽기", textResult.ContentDescription);
        Assert.Contains("생략", textResult.Sections[0].Text);
        var binaryResult = service.Load(binary);
        Assert.Equal(PreviewStateKind.TextLoaded, binaryResult.State);
        Assert.Equal(32 * 1024 * 1024, binaryResult.FileSizeBytes);
        Assert.Contains("4D 5A 00 FE", binaryResult.Sections[1].Text);
        Assert.DoesNotContain("00000400", binaryResult.Sections[1].Text);
        var folder = service.Load(Path.GetDirectoryName(text));
        Assert.Equal("폴더 내용", folder.ContentDescription);
        Assert.Contains("README", Assert.Single(folder.Sections).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tar_ListsEntriesAndReadsFromTheSnapshotWithoutExtracting(bool compressed)
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath(compressed ? "sample.tar.gz" : "sample.tar");
        using (var file = File.Create(path))
        using (var archiveStream = compressed ? new GZipStream(file, CompressionMode.Compress) : (Stream)file)
        using (var tar = new TarWriter(archiveStream))
        using (var contents = new MemoryStream("text inside tar"u8.ToArray()))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "readme") { DataStream = contents });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "link.txt") { LinkName = "elsewhere.txt" });
        }

        var service = new FilePreviewService();
        var result = service.Load(path);
        var archive = Assert.IsType<ArchivePreview>(result.Archive);
        Assert.Equal("TAR", archive.Format);
        File.Delete(path);
        Assert.Contains("text inside tar", service.LoadArchiveEntry(archive, archive.Entries[0]).Sections[0].Text);
        Assert.Contains("다른 항목", service.LoadArchiveEntry(archive, archive.Entries[1]).Sections[0].Text);
        Assert.False(File.Exists(temp.GetPath("readme")));
    }

    [Fact]
    public void SevenZip_ListsAndPreviewsAnIndependentEntry()
    {
        using var temp = new TempTestDirectory();
        // Created with Windows tar.exe --format=7zip from a single file named readme.
        const string sample = "N3q8ryccAANIBYJkEgAAAAAAAABkAAAAAAAAAFXdzRUAG56AB2hMkZu2msL//0kEAAABBAYAAQkSAAcLAQABIwMBAQVdAACAAAwHAAgKAdo9H4EAAAUBEQ8AcgBlAGEAZABtAGUAAAAUCgEACEx1m8Vt3QESCgEACEx1m8Vt3QETCgEACEx1m8Vt3QEVBgEAIIC2gQAA";
        var path = temp.GetPath("sample.7z");
        File.WriteAllBytes(path, Convert.FromBase64String(sample));
        var service = new FilePreviewService();
        var archive = Assert.IsType<ArchivePreview>(service.Load(path).Archive);
        Assert.Equal("7Z", archive.Format);
        var entry = Assert.Single(archive.Entries);
        Assert.Equal("readme", entry.Name);
        Assert.Contains("7z text", service.LoadArchiveEntry(archive, entry).Sections[0].Text);
    }

    [Fact]
    public void TrueType_RendersFontSamplesFromALocalFile()
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath("local-font.ttf");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf"), path);
        Preview(window =>
        {
            window.ViewModel.OpenFile(path);
            Assert.Equal(SpecializedPreviewKind.Font, window.ViewModel.CurrentResult.SpecializedKind);
            Assert.True(Complete(window.WaitForSpecializedPreviewAsync()), Status(window));
            var content = Host(window).Children.OfType<ScrollViewer>().Single();
            Assert.NotEmpty(Assert.IsType<StackPanel>(content.Content).Children.OfType<Image>());
        });
    }

    [Fact]
    public void Pdf_RendersPagesAndUpdatesThePageNumber()
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath("preview.pdf");
        File.WriteAllBytes(path, Pdf());
        Preview(window =>
        {
            var model = window.ViewModel;
            model.OpenFile(path);
            Assert.Equal(SpecializedPreviewKind.Pdf, model.CurrentResult.SpecializedKind);
            Assert.True(Complete(window.WaitForSpecializedPreviewAsync()), Status(window));
            Assert.True(window.VerifyRenderedState());
            var layout = Host(window).Children.OfType<DockPanel>().Single();
            var image = layout.Children.OfType<Image>().Single();
            var first = Assert.IsAssignableFrom<BitmapSource>(image.Source);
            Assert.True(first.PixelWidth > 0);
            var toolbar = layout.Children.OfType<StackPanel>().Single();
            var label = toolbar.Children.OfType<TextBlock>().Single();
            Assert.Equal("1 / 2", label.Text);
            var next = toolbar.Children.OfType<Button>().Last();
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            Complete(WaitForNextPage());
            Assert.Equal("2 / 2", label.Text);
            Assert.NotSame(first, image.Source);
            Assert.False(model.CanExecuteConversion);

            async Task<bool> WaitForNextPage()
            {
                while (next.IsEnabled == false && label.Text == "1 / 2") await Task.Delay(25, timeout.Token);
                return true;
            }
        });
    }

    [Fact]
    public void Svg_RendersAsAnImageAndRejectsDocumentTypes()
    {
        using var temp = new TempTestDirectory();
        var svg = temp.GetPath("preview.svg");
        File.WriteAllText(svg, """
            <svg xmlns="http://www.w3.org/2000/svg" width="40" height="30" onload="throw Error('SVG ran as a document')">
              <rect width="40" height="30" fill="#1020f0"/>
            </svg>
            """);
        var dtd = temp.GetPath("dtd.svg");
        File.WriteAllText(dtd, """<!DOCTYPE svg SYSTEM "https://blocked.invalid/"><svg xmlns="http://www.w3.org/2000/svg"/>""");
        Assert.Equal(PreviewStateKind.Error, new FilePreviewService().Load(dtd).State);
        Preview(window =>
        {
            window.ViewModel.OpenFile(svg);
            Assert.True(Complete(window.WaitForSpecializedPreviewAsync()), Status(window));
            Assert.True(window.VerifyRenderedState());
            var web = Host(window).Children.OfType<WebView2>().Single();
            Assert.Equal("[40,30]", Complete(web.ExecuteScriptAsync(
                "Array.from(document.images, i => [i.naturalWidth, i.naturalHeight])[0]")));
        });
    }

    [Theory]
    [InlineData("animation.gif", "GIF 애니메이션")]
    [InlineData("animation.png", "APNG 애니메이션")]
    public void AnimatedRasterImages_UseTheRenderingSurface(string fileName, string description)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        Preview(window =>
        {
            window.ViewModel.OpenFile(path);
            Assert.Equal(description, window.ViewModel.ContentDescription);
            Assert.True(Complete(window.WaitForSpecializedPreviewAsync()), Status(window));
            Assert.True(window.VerifyRenderedState());
            var web = Host(window).Children.OfType<WebView2>().Single();
            Assert.Equal("[40,32]", Complete(web.ExecuteScriptAsync(
                "Array.from(document.images, image => [image.naturalWidth, image.naturalHeight])[0]")));
        });
    }

    [Fact]
    public void Wav_OpensWithExplicitPlaybackControls()
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath("audio.wav");
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            const int samples = 8000;
            writer.Write("RIFF"u8);
            writer.Write(36 + samples);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write(samples);
            writer.Write(samples);
            writer.Write((ushort)1);
            writer.Write((ushort)8);
            writer.Write("data"u8);
            writer.Write(samples);
            writer.Write(Enumerable.Repeat((byte)128, samples).ToArray());
        }
        Preview(window =>
        {
            window.ViewModel.OpenFile(path);
            Assert.Equal(SpecializedPreviewKind.Media, window.ViewModel.CurrentResult.SpecializedKind);
            Assert.True(Complete(window.WaitForSpecializedPreviewAsync()), Status(window));
            var toolbar = Host(window).Children.OfType<DockPanel>().Single().Children.OfType<StackPanel>().Single();
            var play = toolbar.Children.OfType<Button>().Single();
            Assert.True(play.IsVisible);
            Assert.Equal("재생", play.Content);
            play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("일시 정지", play.Content);
            play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("재생", play.Content);
        });
        File.Delete(path);
    }

    private static void Preview(Action<MainWindow> check) => StaTestRunner.Run(() =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow(new MainWindowViewModel()) { ShowActivated = false, ShowInTaskbar = false };
        try { window.Show(); check(window); }
        finally { window.Close(); }
    });

    private static Grid Host(MainWindow window) => (Grid)((Grid)window.FindName("SpecializedViewport")).Children[0];

    private static string Status(MainWindow window) => Host(window).Children.OfType<TextBlock>().First().Text;

    private static T Complete<T>(Task<T> task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var dispatcher = Dispatcher.CurrentDispatcher;
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            timeout.Tick += (_, _) => frame.Continue = false;
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false));
            timeout.Start();
            Dispatcher.PushFrame(frame);
            timeout.Stop();
        }
        Assert.True(task.IsCompleted, "미리보기가 제한 시간 안에 완료되지 않았습니다.");
        return task.GetAwaiter().GetResult();
    }

    private static byte[] Pdf()
    {
        using var stream = new MemoryStream();
        void Write(string value) => stream.Write(Encoding.ASCII.GetBytes(value));
        Write("%PDF-1.4\n");
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Count 2 /Kids [3 0 R 4 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 80] /Contents 5 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 80 100] /Contents 6 0 R >>",
            "<< /Length 21 >>\nstream\n1 0 0 rg 5 5 50 50 re f\nendstream",
            "<< /Length 21 >>\nstream\n0 1 0 rg 5 5 50 50 re f\nendstream",
        };
        var positions = new List<long>();
        for (var i = 0; i < objects.Length; i++)
        {
            positions.Add(stream.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = stream.Position;
        Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var position in positions) Write($"{position:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return stream.ToArray();
    }
}
