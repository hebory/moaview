using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using MoaView.ViewModels;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MoaView.Tests;

public sealed class MainWindowUiTests
{
    private sealed class SequenceFileDialogService(params string?[] paths) : IFileDialogService
    {
        private readonly Queue<string?> _paths = new(paths);

        public string? PickFile(Window? owner = null) =>
            _paths.Count > 0 ? _paths.Dequeue() : null;
    }

    [Fact]
    public void MainWindow_ShownWindowTransitionsAcrossEmptyImageUnsupportedAndErrorStates()
    {
        using var temp = new TempTestDirectory();
        var tiffPath = SampleImageFactory.WriteValidImage(
            temp.GetPath("scan.tiff"),
            ImageFormatKind.Tiff,
            width: 72,
            height: 48);
        var unsupportedPath = temp.GetPath("manual.pdf");
        File.WriteAllText(unsupportedPath, "%PDF-1.7 sample");
        var corruptedPath = temp.GetPath("damaged.jpg");
        File.WriteAllBytes(corruptedPath, [0xFF, 0xD8, 0xFF, 0x00, 0x11]);
        var recoveryPngPath = SampleImageFactory.WriteValidImage(
            temp.GetPath("recovered.png"),
            ImageFormatKind.Png,
            width: 60,
            height: 45);

        StaTestRunner.Run(() =>
        {
            var dialog = new SequenceFileDialogService(tiffPath, unsupportedPath, corruptedPath, recoveryPngPath);
            var viewModel = new MainWindowViewModel(new ImagePreviewService(), dialog);
            var window = new MainWindow(viewModel);

            try
            {
                ShowWindowForTest(window);

                Assert.True(window.IsLoaded);
                Assert.True(window.IsVisible);
                Assert.NotNull(PresentationSource.FromVisual(window));

                var emptyPanel = (Border)window.FindName("EmptyStatePanel")!;
                var imageViewport = (Border)window.FindName("ImageViewport")!;
                var previewImage = (Image)window.FindName("PreviewImage")!;
                var noticePanel = (Border)window.FindName("NoticePanel")!;
                var noticeBadge = (TextBlock)window.FindName("NoticeBadgeText")!;
                var noticeMessage = (TextBlock)window.FindName("NoticeMessageText")!;
                var metadataText = (TextBlock)window.FindName("ImageMetadataText")!;
                var formatsBanner = (TextBlock)window.FindName("SupportedFormatsBanner")!;
                var openButton = (Button)window.FindName("OpenFileButton")!;

                // 1) 초기 빈 화면 실제 창 표시 검증
                Assert.True(window.VerifyRenderedState());
                Assert.True(emptyPanel.IsVisible);
                Assert.False(imageViewport.IsVisible);
                Assert.False(noticePanel.IsVisible);
                Assert.Contains("JPEG, PNG, BMP, GIF, TIFF", formatsBanner.Text, StringComparison.Ordinal);

                // 2) '파일 선택...' 버튼 명령으로 지원 이미지(TIFF) 열기 및 렌더링 검증
                openButton.Command.Execute(openButton.CommandParameter);
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.False(emptyPanel.IsVisible);
                Assert.True(imageViewport.IsVisible);
                Assert.True(previewImage.IsVisible);
                Assert.False(noticePanel.IsVisible);
                var renderedBitmap = Assert.IsAssignableFrom<BitmapSource>(previewImage.Source);
                Assert.Equal(72, renderedBitmap.PixelWidth);
                Assert.Equal(48, renderedBitmap.PixelHeight);
                Assert.Contains("TIFF", metadataText.Text, StringComparison.Ordinal);
                Assert.Contains("72 × 48 px", metadataText.Text, StringComparison.Ordinal);

                // 3) 미지원 형식(.pdf) 선택 시 이전 이미지가 해제되고 안내 패널이 표시되는지 검증
                openButton.Command.Execute(openButton.CommandParameter);
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.False(emptyPanel.IsVisible);
                Assert.False(imageViewport.IsVisible);
                Assert.Null(previewImage.Source);
                Assert.True(noticePanel.IsVisible);
                Assert.Equal("미지원 형식", noticeBadge.Text);
                Assert.Contains(".pdf", noticeMessage.Text, StringComparison.OrdinalIgnoreCase);

                // 4) 손상된 이미지(.jpg) 선택 시 예외 없이 오류 안내 패널이 표시되는지 검증
                openButton.Command.Execute(openButton.CommandParameter);
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.False(imageViewport.IsVisible);
                Assert.Null(previewImage.Source);
                Assert.True(noticePanel.IsVisible);
                Assert.Equal("열기 실패", noticeBadge.Text);
                Assert.Contains("손상", noticeMessage.Text, StringComparison.Ordinal);

                // 5) 오류 안내 이후 다시 지원 이미지(.png) 선택 시 미리보기가 정상 복구되는지 검증
                openButton.Command.Execute(openButton.CommandParameter);
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.False(emptyPanel.IsVisible);
                Assert.False(noticePanel.IsVisible);
                Assert.True(imageViewport.IsVisible);
                var recoveredBitmap = Assert.IsAssignableFrom<BitmapSource>(previewImage.Source);
                Assert.Equal(60, recoveredBitmap.PixelWidth);
                Assert.Equal(45, recoveredBitmap.PixelHeight);
                Assert.Equal("recovered.png - 모아뷰 (MoaView)", window.Title);
            }
            finally
            {
                window.Close();
                FlushDispatcher();
            }
        });
    }

    [Fact]
    public void MainWindow_ShownWindowPreloadedStateAndFileDrop_RenderExpectedControls()
    {
        using var temp = new TempTestDirectory();
        var unsupportedPath = temp.GetPath("notes.txt");
        File.WriteAllText(unsupportedPath, "plain text");
        var droppedGifPath = SampleImageFactory.WriteValidImage(
            temp.GetPath("dropped.gif"),
            ImageFormatKind.Gif,
            width: 40,
            height: 28);

        StaTestRunner.Run(() =>
        {
            var viewModel = new MainWindowViewModel();
            viewModel.OpenFile(unsupportedPath);

            var window = new MainWindow(viewModel);
            try
            {
                ShowWindowForTest(window);

                var noticePanel = (Border)window.FindName("NoticePanel")!;
                var imageViewport = (Border)window.FindName("ImageViewport")!;
                var previewImage = (Image)window.FindName("PreviewImage")!;

                Assert.True(window.VerifyRenderedState());
                Assert.True(noticePanel.IsVisible);
                Assert.False(imageViewport.IsVisible);

                var dropData = new DataObject(DataFormats.FileDrop, new[] { droppedGifPath });
                Assert.True(window.TryOpenDataObject(dropData));
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.False(noticePanel.IsVisible);
                Assert.True(imageViewport.IsVisible);
                var bitmap = Assert.IsAssignableFrom<BitmapSource>(previewImage.Source);
                Assert.Equal(40, bitmap.PixelWidth);
                Assert.Equal(28, bitmap.PixelHeight);
                Assert.Equal("dropped.gif - 모아뷰 (MoaView)", window.Title);
            }
            finally
            {
                window.Close();
                FlushDispatcher();
            }
        });
    }

    [Fact]
    public void App_ParseStartupOptionsAndResolveInitialFilePath_HandleSwitchesAndArguments()
    {
        Assert.Equal("C:\\sample.png", App.ResolveInitialFilePath(["C:\\sample.png"], ["C:\\other.jpg"]));
        Assert.Equal("C:\\fallback.jpg", App.ResolveInitialFilePath([], ["C:\\fallback.jpg"]));
        Assert.Null(App.ResolveInitialFilePath(["   "], []));
        Assert.Null(App.ResolveInitialFilePath(null, null));

        var verifyWithFile = App.ParseStartupOptions([App.VerifyStartupSwitch, "C:\\check.png"], []);
        Assert.True(verifyWithFile.VerifyStartupAndClose);
        Assert.Equal("C:\\check.png", verifyWithFile.InitialFilePath);

        var verifyWithoutFile = App.ParseStartupOptions([], ["  --VERIFY-STARTUP  "]);
        Assert.True(verifyWithoutFile.VerifyStartupAndClose);
        Assert.Null(verifyWithoutFile.InitialFilePath);
    }

    [Fact]
    public void MoaViewExecutable_VerifyStartup_LaunchesBuiltWinExeAndRendersStates()
    {
        var exePath = ResolveMoaViewExecutablePath();
        Assert.True(File.Exists(exePath), $"빌드된 MoaView.exe를 찾을 수 없습니다: {exePath}");

        using var temp = new TempTestDirectory();
        var pngPath = SampleImageFactory.WriteValidImage(
            temp.GetPath("launch-sample.png"),
            ImageFormatKind.Png,
            width: 64,
            height: 48);
        var pdfPath = temp.GetPath("unsupported.pdf");
        File.WriteAllText(pdfPath, "%PDF-1.7 unsupported");
        var corruptedJpgPath = temp.GetPath("corrupted.jpg");
        File.WriteAllBytes(corruptedJpgPath, [0xFF, 0xD8, 0x00, 0x22]);

        var emptyRun = RunStartupVerification(exePath);
        Assert.Contains("MOAVIEW_STARTUP_VERIFIED=True", emptyRun, StringComparison.Ordinal);
        Assert.Contains("|STATE=Empty|", emptyRun, StringComparison.Ordinal);
        Assert.Contains("|VISIBLE=True|", emptyRun, StringComparison.Ordinal);
        Assert.Contains("|TITLE=모아뷰 (MoaView)|", emptyRun, StringComparison.Ordinal);

        var imageRun = RunStartupVerification(exePath, pngPath);
        Assert.Contains("MOAVIEW_STARTUP_VERIFIED=True", imageRun, StringComparison.Ordinal);
        Assert.Contains("|STATE=ImageLoaded|", imageRun, StringComparison.Ordinal);
        Assert.Contains("|VISIBLE=True|", imageRun, StringComparison.Ordinal);
        Assert.Contains("|TITLE=launch-sample.png - 모아뷰 (MoaView)|", imageRun, StringComparison.Ordinal);
        Assert.Contains("|PIXELS=64x48", imageRun, StringComparison.Ordinal);

        var unsupportedRun = RunStartupVerification(exePath, pdfPath);
        Assert.Contains("MOAVIEW_STARTUP_VERIFIED=True", unsupportedRun, StringComparison.Ordinal);
        Assert.Contains("|STATE=UnsupportedFormat|", unsupportedRun, StringComparison.Ordinal);
        Assert.Contains("|VISIBLE=True|", unsupportedRun, StringComparison.Ordinal);
        Assert.Contains("|BADGE=미지원 형식|", unsupportedRun, StringComparison.Ordinal);

        var errorRun = RunStartupVerification(exePath, corruptedJpgPath);
        Assert.Contains("MOAVIEW_STARTUP_VERIFIED=True", errorRun, StringComparison.Ordinal);
        Assert.Contains("|STATE=Error|", errorRun, StringComparison.Ordinal);
        Assert.Contains("|VISIBLE=True|", errorRun, StringComparison.Ordinal);
        Assert.Contains("|BADGE=열기 실패|", errorRun, StringComparison.Ordinal);
    }

    private static string RunStartupVerification(string exePath, string? targetPath = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(App.VerifyStartupSwitch);
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            startInfo.ArgumentList.Add(targetPath);
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(15_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 종료 경쟁 상태 무시
            }

            Assert.Fail("MoaView.exe --verify-startup 프로세스가 제한 시간(15초) 내에 종료되지 않았습니다.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult().Trim();
        var stderr = stderrTask.GetAwaiter().GetResult().Trim();

        Assert.True(
            process.ExitCode == 0,
            $"MoaView.exe 종료 코드가 0이 아닙니다 (ExitCode={process.ExitCode}). STDOUT={stdout} STDERR={stderr}");

        return stdout;
    }

    private static string ResolveMoaViewExecutablePath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        var tfmName = current.Name;
        var configName = current.Parent?.Name ?? "Debug";

        for (var cursor = current; cursor is not null; cursor = cursor.Parent)
        {
            var slnPath = Path.Combine(cursor.FullName, "MoaView.sln");
            if (!File.Exists(slnPath))
            {
                continue;
            }

            var exactCandidate = Path.Combine(
                cursor.FullName,
                "src",
                "MoaView",
                "bin",
                configName,
                tfmName,
                "MoaView.exe");
            var exactRuntimeConfig = Path.Combine(
                cursor.FullName,
                "src",
                "MoaView",
                "bin",
                configName,
                tfmName,
                "MoaView.runtimeconfig.json");
            if (File.Exists(exactCandidate) && File.Exists(exactRuntimeConfig))
            {
                return exactCandidate;
            }

            var binRoot = Path.Combine(cursor.FullName, "src", "MoaView", "bin");
            if (Directory.Exists(binRoot))
            {
                foreach (var candidate in Directory.EnumerateFiles(binRoot, "MoaView.exe", SearchOption.AllDirectories))
                {
                    var runtimeConfig = Path.Combine(Path.GetDirectoryName(candidate)!, "MoaView.runtimeconfig.json");
                    if (File.Exists(runtimeConfig))
                    {
                        return candidate;
                    }
                }
            }

            return exactCandidate;
        }

        throw new FileNotFoundException("저장소 루트(MoaView.sln)에서 MoaView.exe 경로를 찾지 못했습니다.");
    }

    private static void ShowWindowForTest(MainWindow window)
    {
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();
        PumpAndRender(window);
    }

    private static void PumpAndRender(MainWindow window)
    {
        window.UpdateLayout();
        FlushDispatcher();
        window.UpdateLayout();
    }

    private static void FlushDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
    }
}
