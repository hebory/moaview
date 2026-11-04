using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private sealed class SequenceSaveFileDialogService(params string?[] paths) : ISaveFileDialogService
    {
        private readonly Queue<string?> _paths = new(paths);

        public int CallCount { get; private set; }

        public string? PickSaveFile(
            string suggestedFilePath,
            ImageFormatKind targetFormat,
            Window? owner = null)
        {
            CallCount++;
            return _paths.Count > 0 ? _paths.Dequeue() : suggestedFilePath;
        }
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

    [Fact]
    public void MainWindow_ZoomAndFitToWindowControls_UpdateRenderedImageSizeAndScrollbars()
    {
        using var temp = new TempTestDirectory();
        var largePngPath = SampleImageFactory.WriteValidImage(
            temp.GetPath("01_large.png"),
            ImageFormatKind.Png,
            width: 1400,
            height: 1000);
        var smallJpgPath = SampleImageFactory.WriteValidImage(
            temp.GetPath("02_small.jpg"),
            ImageFormatKind.Jpeg,
            width: 120,
            height: 80);

        StaTestRunner.Run(() =>
        {
            var viewModel = new MainWindowViewModel();
            viewModel.OpenFile(largePngPath);
            var window = new MainWindow(viewModel);

            try
            {
                ShowWindowForTest(window);

                var scrollViewer = (ScrollViewer)window.FindName("ImageScrollViewer")!;
                var previewImage = (Image)window.FindName("PreviewImage")!;
                var zoomInButton = (Button)window.FindName("ZoomInButton")!;
                var zoomOutButton = (Button)window.FindName("ZoomOutButton")!;
                var actualSizeButton = (Button)window.FindName("ActualSizeButton")!;
                var fitButton = (Button)window.FindName("FitToWindowButton")!;
                var zoomStatusText = (TextBlock)window.FindName("ZoomStatusText")!;

                // 1) 큰 이미지(1400x1000) 초기 화면 맞춤 렌더링 확인
                Assert.True(window.VerifyRenderedState());
                Assert.True(viewModel.IsFitToWindow);
                Assert.Contains("화면 맞춤", zoomStatusText.Text, StringComparison.Ordinal);
                Assert.True(previewImage.ActualWidth <= scrollViewer.ActualWidth + 1.0);
                Assert.True(previewImage.ActualHeight <= scrollViewer.ActualHeight + 1.0);
                Assert.Equal(0.0, scrollViewer.ScrollableWidth, precision: 1);
                Assert.Equal(0.0, scrollViewer.ScrollableHeight, precision: 1);
                var initialFitWidth = previewImage.ActualWidth;
                var initialFitHeight = previewImage.ActualHeight;

                // 2) '100%' 버튼 실행 -> 실제 렌더링 크기가 1400x1000으로 전환되고 스크롤이 활성화됨
                actualSizeButton.Command.Execute(null);
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.False(viewModel.IsFitToWindow);
                Assert.Equal("100%", zoomStatusText.Text);
                Assert.Equal(1400.0, previewImage.ActualWidth, precision: 1);
                Assert.Equal(1000.0, previewImage.ActualHeight, precision: 1);
                Assert.True(scrollViewer.ScrollableWidth > 200.0);
                Assert.True(scrollViewer.ScrollableHeight > 200.0);

                scrollViewer.ScrollToHorizontalOffset(180.0);
                scrollViewer.ScrollToVerticalOffset(120.0);
                PumpAndRender(window);
                Assert.Equal(180.0, scrollViewer.HorizontalOffset, precision: 1);
                Assert.Equal(120.0, scrollViewer.VerticalOffset, precision: 1);

                // 3) '확대 +' 버튼 및 Ctrl+휠 동작 -> 배율에 비례해 실제 컨트롤 크기 증감
                zoomInButton.Command.Execute(null);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());
                Assert.Equal("125%", zoomStatusText.Text);
                Assert.Equal(1750.0, previewImage.ActualWidth, precision: 1);
                Assert.Equal(1250.0, previewImage.ActualHeight, precision: 1);

                Assert.True(window.HandleZoomMouseWheel(120));
                PumpAndRender(window);
                Assert.Equal("150%", zoomStatusText.Text);
                Assert.Equal(2100.0, previewImage.ActualWidth, precision: 1);
                Assert.Equal(1500.0, previewImage.ActualHeight, precision: 1);

                zoomOutButton.Command.Execute(null);
                zoomOutButton.Command.Execute(null);
                zoomOutButton.Command.Execute(null);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());
                Assert.Equal("75%", zoomStatusText.Text);
                Assert.Equal(1050.0, previewImage.ActualWidth, precision: 1);
                Assert.Equal(750.0, previewImage.ActualHeight, precision: 1);

                // 4) '화면 맞춤' 버튼 클릭 -> 뷰포트 맞춤 크기로 복귀하고 스크롤 오프셋이 0으로 초기화됨
                fitButton.Command.Execute(null);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());
                Assert.True(viewModel.IsFitToWindow);
                Assert.Equal(initialFitWidth, previewImage.ActualWidth, precision: 1);
                Assert.Equal(initialFitHeight, previewImage.ActualHeight, precision: 1);
                Assert.Equal(0.0, scrollViewer.HorizontalOffset, precision: 1);
                Assert.Equal(0.0, scrollViewer.VerticalOffset, precision: 1);

                // 5) 화면 맞춤 상태에서 바로 확대/축소 시 현재 화면 크기 기준으로 자연스럽게 커지고 작아짐
                zoomInButton.Command.Execute(null);
                PumpAndRender(window);
                Assert.True(previewImage.ActualWidth > initialFitWidth);
                Assert.True(previewImage.ActualHeight > initialFitHeight);

                RaisePreviewKeyDown(window, Key.F);
                PumpAndRender(window);
                Assert.True(viewModel.IsFitToWindow);

                zoomOutButton.Command.Execute(null);
                PumpAndRender(window);
                Assert.True(previewImage.ActualWidth < initialFitWidth);
                Assert.True(previewImage.ActualHeight < initialFitHeight);

                // 6) 작은 이미지(120x80)에서도 화면 맞춤 <-> 100% 및 확대/축소가 일관되게 동작
                viewModel.OpenFile(smallJpgPath);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());
                var smallFitWidth = previewImage.ActualWidth;
                Assert.True(smallFitWidth > 120.0);

                zoomInButton.Command.Execute(null);
                PumpAndRender(window);
                Assert.True(previewImage.ActualWidth > smallFitWidth);

                RaisePreviewKeyDown(window, Key.D0);
                PumpAndRender(window);
                Assert.Equal(120.0, previewImage.ActualWidth, precision: 1);
                Assert.Equal(80.0, previewImage.ActualHeight, precision: 1);
            }
            finally
            {
                window.Close();
                FlushDispatcher();
            }
        });
    }

    [Fact]
    public void MainWindow_RotationControlsAndShortcuts_RotatePixelsAndSwapRenderedAspectRatio()
    {
        using var temp = new TempTestDirectory();
        var red = Color.FromRgb(220, 45, 45);
        var green = Color.FromRgb(45, 195, 75);
        var blue = Color.FromRgb(45, 95, 225);
        var yellow = Color.FromRgb(235, 200, 40);

        var asymmetricPng = SampleImageFactory.WriteQuadrantImage(
            temp.GetPath("quadrant.png"),
            ImageFormatKind.Png,
            width: 160,
            height: 80,
            topLeft: red,
            topRight: green,
            bottomRight: blue,
            bottomLeft: yellow);

        StaTestRunner.Run(() =>
        {
            var viewModel = new MainWindowViewModel();
            viewModel.OpenFile(asymmetricPng);
            var window = new MainWindow(viewModel);

            try
            {
                ShowWindowForTest(window);

                var previewImage = (Image)window.FindName("PreviewImage")!;
                var rotateRightButton = (Button)window.FindName("RotateRightButton")!;
                var rotateLeftButton = (Button)window.FindName("RotateLeftButton")!;
                var actualSizeButton = (Button)window.FindName("ActualSizeButton")!;
                var fitButton = (Button)window.FindName("FitToWindowButton")!;
                var rotationStatusText = (TextBlock)window.FindName("RotationStatusText")!;

                // 1) 화면 맞춤 상태에서 가로형(ActualWidth > ActualHeight) -> 우회전 후 세로형(ActualHeight > ActualWidth)
                Assert.True(window.VerifyRenderedState());
                Assert.Equal("0°", rotationStatusText.Text);
                Assert.True(previewImage.ActualWidth > previewImage.ActualHeight);

                rotateRightButton.Command.Execute(null);
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.Equal("90°", rotationStatusText.Text);
                Assert.True(previewImage.ActualHeight > previewImage.ActualWidth);

                // 2) 100% 실제 크기 전환 시 80x160으로 렌더링되고 사분면 픽셀 색상이 시계 방향 90도 이동함
                actualSizeButton.Command.Execute(null);
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.Equal(80.0, previewImage.ActualWidth, precision: 1);
                Assert.Equal(160.0, previewImage.ActualHeight, precision: 1);

                var rotated90 = Assert.IsAssignableFrom<BitmapSource>(previewImage.Source);
                Assert.Equal(red, SampleImageFactory.ReadPixelColor(rotated90, x: 60, y: 20));
                Assert.Equal(green, SampleImageFactory.ReadPixelColor(rotated90, x: 60, y: 140));
                Assert.Equal(blue, SampleImageFactory.ReadPixelColor(rotated90, x: 20, y: 140));
                Assert.Equal(yellow, SampleImageFactory.ReadPixelColor(rotated90, x: 20, y: 20));

                // 3) 키보드 단축키 'R'로 180도 회전 -> 160x80, 좌상단 빨강이 우하단으로 이동
                RaisePreviewKeyDown(window, Key.R);
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.Equal("180°", rotationStatusText.Text);
                Assert.Equal(160.0, previewImage.ActualWidth, precision: 1);
                Assert.Equal(80.0, previewImage.ActualHeight, precision: 1);
                var rotated180 = Assert.IsAssignableFrom<BitmapSource>(previewImage.Source);
                Assert.Equal(red, SampleImageFactory.ReadPixelColor(rotated180, x: 140, y: 60));

                // 4) 키보드 단축키 'L' 및 '↺ 좌회전' 버튼으로 90도 -> 0도 원위치 복귀
                RaisePreviewKeyDown(window, Key.L);
                PumpAndRender(window);
                Assert.Equal("90°", rotationStatusText.Text);

                rotateLeftButton.Command.Execute(null);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());
                Assert.Equal("0°", rotationStatusText.Text);
                Assert.Equal(160.0, previewImage.ActualWidth, precision: 1);
                Assert.Equal(80.0, previewImage.ActualHeight, precision: 1);

                fitButton.Command.Execute(null);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());
                Assert.True(previewImage.ActualWidth > previewImage.ActualHeight);
            }
            finally
            {
                window.Close();
                FlushDispatcher();
            }
        });
    }

    [Fact]
    public void MainWindow_FolderNavigationButtonsAndArrowKeys_TraverseAllFormatsAndRecoverFromCorruptedFile()
    {
        using var temp = new TempTestDirectory();
        File.WriteAllText(temp.GetPath("00_ignore.txt"), "plain text");
        var jpgPath = SampleImageFactory.WriteValidImage(temp.GetPath("01_photo.jpg"), ImageFormatKind.Jpeg, 90, 60);
        var pngPath = SampleImageFactory.WriteValidImage(temp.GetPath("02_graphic.png"), ImageFormatKind.Png, 100, 70);
        var brokenBmpPath = temp.GetPath("03_corrupted.bmp");
        File.WriteAllBytes(brokenBmpPath, [0x42, 0x4D, 0x00, 0xFF, 0x10]);
        var bmpPath = SampleImageFactory.WriteValidImage(temp.GetPath("04_bitmap.bmp"), ImageFormatKind.Bmp, 64, 48);
        var gifPath = SampleImageFactory.WriteValidImage(temp.GetPath("05_anim.gif"), ImageFormatKind.Gif, 56, 42);
        var tiffPath = SampleImageFactory.WriteValidImage(temp.GetPath("06_scan.tiff"), ImageFormatKind.Tiff, 84, 52);
        File.WriteAllText(temp.GetPath("99_ignore.zip"), "PK");

        StaTestRunner.Run(() =>
        {
            var viewModel = new MainWindowViewModel();
            viewModel.OpenFile(jpgPath);
            var window = new MainWindow(viewModel);

            try
            {
                ShowWindowForTest(window);

                var prevButton = (Button)window.FindName("PreviousImageButton")!;
                var nextButton = (Button)window.FindName("NextImageButton")!;
                var folderPositionText = (TextBlock)window.FindName("FolderPositionText")!;
                var previewImage = (Image)window.FindName("PreviewImage")!;
                var noticePanel = (Border)window.FindName("NoticePanel")!;
                var noticeBadge = (TextBlock)window.FindName("NoticeBadgeText")!;
                var metadataText = (TextBlock)window.FindName("ImageMetadataText")!;

                // 1) 첫 번째 이미지(JPEG, 1 / 6) 확인 후 회전·확대 적용 뒤 다음 이미지(PNG)로 이동
                Assert.True(window.VerifyRenderedState());
                Assert.Equal("1 / 6", folderPositionText.Text);
                Assert.True(prevButton.IsEnabled);
                Assert.True(nextButton.IsEnabled);
                Assert.Contains("JPEG", metadataText.Text, StringComparison.Ordinal);

                viewModel.RotateClockwise();
                viewModel.ZoomIn();
                PumpAndRender(window);
                Assert.Equal(90, viewModel.RotationDegrees);
                Assert.False(viewModel.IsFitToWindow);

                nextButton.Command.Execute(null);
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.Equal(pngPath, viewModel.CurrentResult.FilePath);
                Assert.Equal("2 / 6", folderPositionText.Text);
                Assert.Equal(0, viewModel.RotationDegrees);
                Assert.True(viewModel.IsFitToWindow);
                Assert.Contains("PNG", metadataText.Text, StringComparison.Ordinal);

                // 2) 오른쪽 방향키로 손상된 BMP(3 / 6) 이동 -> 비정상 종료 없이 오류 안내 표시 + 탐색 버튼 활성 유지
                RaisePreviewKeyDown(window, Key.Right);
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.Equal(PreviewStateKind.Error, viewModel.State);
                Assert.Equal("3 / 6", folderPositionText.Text);
                Assert.True(noticePanel.IsVisible);
                Assert.Null(previewImage.Source);
                Assert.Equal("열기 실패", noticeBadge.Text);
                Assert.True(nextButton.IsEnabled);
                Assert.True(prevButton.IsEnabled);

                // 3) 손상 파일에서 다시 오른쪽 방향키로 정상 BMP(4 / 6) -> GIF(5 / 6) -> TIFF(6 / 6) 순회 복구
                RaisePreviewKeyDown(window, Key.Right);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());
                Assert.Equal(bmpPath, viewModel.CurrentResult.FilePath);
                Assert.Equal("4 / 6", folderPositionText.Text);
                Assert.Contains("BMP", metadataText.Text, StringComparison.Ordinal);

                RaisePreviewKeyDown(window, Key.Right);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());
                Assert.Equal(gifPath, viewModel.CurrentResult.FilePath);
                Assert.Equal("5 / 6", folderPositionText.Text);
                Assert.Contains("GIF", metadataText.Text, StringComparison.Ordinal);

                RaisePreviewKeyDown(window, Key.Right);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());
                Assert.Equal(tiffPath, viewModel.CurrentResult.FilePath);
                Assert.Equal("6 / 6", folderPositionText.Text);
                Assert.Contains("TIFF", metadataText.Text, StringComparison.Ordinal);

                // 4) 마지막(6 / 6)에서 다음 클릭 -> 첫 번째(1 / 6) 순환, 다시 왼쪽 방향키로 마지막(6 / 6) 역순환
                nextButton.Command.Execute(null);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());
                Assert.Equal(jpgPath, viewModel.CurrentResult.FilePath);
                Assert.Equal("1 / 6", folderPositionText.Text);

                RaisePreviewKeyDown(window, Key.Left);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());
                Assert.Equal(tiffPath, viewModel.CurrentResult.FilePath);
                Assert.Equal("6 / 6", folderPositionText.Text);

                prevButton.Command.Execute(null);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());
                Assert.Equal(gifPath, viewModel.CurrentResult.FilePath);
                Assert.Equal("5 / 6", folderPositionText.Text);
            }
            finally
            {
                window.Close();
                FlushDispatcher();
            }
        });
    }

    [Fact]
    public void MainWindow_ImageConversionFlow_ConvertsAcrossJpegPngBmpAndReopensVerifiedOutputs()
    {
        using var temp = new TempTestDirectory();
        var red = Color.FromRgb(220, 45, 45);
        var green = Color.FromRgb(45, 190, 75);
        var blue = Color.FromRgb(45, 95, 225);
        var yellow = Color.FromRgb(235, 200, 40);

        var sourcePngPath = SampleImageFactory.WriteQuadrantImage(
            temp.GetPath("01_source.png"),
            ImageFormatKind.Png,
            width: 120,
            height: 80,
            topLeft: red,
            topRight: green,
            bottomRight: blue,
            bottomLeft: yellow);
        var originalPngBytes = File.ReadAllBytes(sourcePngPath);

        var outBmpPath = temp.GetPath("02_from_png.bmp");
        var outJpgPath = temp.GetPath("03_from_bmp.jpg");
        var outPngPath = temp.GetPath("04_from_jpg.png");
        var outJpgFromPngPath = temp.GetPath("05_from_png.jpg");
        var outBmpFromJpgPath = temp.GetPath("06_from_jpg.bmp");
        var outPngFromBmpPath = temp.GetPath("07_from_bmp.png");

        StaTestRunner.Run(() =>
        {
            var openDialog = new SequenceFileDialogService();
            var saveDialog = new SequenceSaveFileDialogService(
                outBmpPath,
                outJpgPath,
                outPngPath,
                outJpgFromPngPath,
                outBmpFromJpgPath,
                outPngFromBmpPath);
            var viewModel = new MainWindowViewModel(
                new ImagePreviewService(),
                openDialog,
                saveDialog);
            viewModel.OpenFile(sourcePngPath);

            var window = new MainWindow(viewModel);
            try
            {
                ShowWindowForTest(window);

                var formatCombo = (ComboBox)window.FindName("ConvertFormatComboBox")!;
                var convertButton = (Button)window.FindName("ConvertImageButton")!;
                var conversionBanner = (Border)window.FindName("ConversionNoticeBanner")!;
                var conversionBadge = (TextBlock)window.FindName("ConversionNoticeBadgeText")!;
                var openConvertedButton = (Button)window.FindName("OpenConvertedFileButton")!;
                var previewImage = (Image)window.FindName("PreviewImage")!;
                var metadataText = (TextBlock)window.FindName("ImageMetadataText")!;
                var folderPositionText = (TextBlock)window.FindName("FolderPositionText")!;

                Assert.True(window.VerifyRenderedState());
                Assert.False(conversionBanner.IsVisible);
                Assert.True(formatCombo.IsEnabled);
                Assert.True(convertButton.IsEnabled);
                Assert.Equal(3, formatCombo.Items.Count);
                Assert.Equal("1 / 1", folderPositionText.Text);

                // 보기 회전·확대 조작 후에도 원본 해상도(120x80) 기준으로 정확히 변환 저장되는지 함께 검증 (C4 + C5)
                viewModel.RotateClockwise();
                viewModel.ZoomIn();
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());

                // 1) PNG -> BMP 변환 후 '결과 파일 열기' 버튼으로 실제 창에 다시 열어 출력 형식·해상도·픽셀 검증
                formatCombo.SelectedItem = viewModel.AvailableConversionFormats.Single(o => o.Format == ImageFormatKind.Bmp);
                PumpAndRender(window);

                convertButton.Command.Execute(convertButton.CommandParameter);
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.True(conversionBanner.IsVisible);
                Assert.Equal("변환 완료", conversionBadge.Text);
                Assert.True(openConvertedButton.IsVisible);
                Assert.Equal("1 / 2", folderPositionText.Text);

                openConvertedButton.Command.Execute(null);
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.False(conversionBanner.IsVisible);
                Assert.Equal(outBmpPath, viewModel.CurrentResult.FilePath);
                Assert.Equal(ImageFormatKind.Bmp, viewModel.CurrentResult.ImageFormat);
                var reopenedBmp = Assert.IsAssignableFrom<BitmapSource>(previewImage.Source);
                Assert.Equal(120, reopenedBmp.PixelWidth);
                Assert.Equal(80, reopenedBmp.PixelHeight);
                Assert.Equal(red, SampleImageFactory.ReadPixelColor(reopenedBmp, x: 25, y: 20));
                Assert.Equal(green, SampleImageFactory.ReadPixelColor(reopenedBmp, x: 95, y: 20));
                Assert.Equal(blue, SampleImageFactory.ReadPixelColor(reopenedBmp, x: 95, y: 60));
                Assert.Equal(yellow, SampleImageFactory.ReadPixelColor(reopenedBmp, x: 25, y: 60));
                Assert.Contains("BMP", metadataText.Text, StringComparison.Ordinal);
                Assert.Contains("120 × 80 px", metadataText.Text, StringComparison.Ordinal);

                // 2) BMP -> JPEG 변환 (Ctrl+Shift+S 단축키) 후 결과 파일 다시 열기 검증
                formatCombo.SelectedItem = viewModel.AvailableConversionFormats.Single(o => o.Format == ImageFormatKind.Jpeg);
                PumpAndRender(window);

                Assert.True(window.HandleKeyInput(Key.S, ModifierKeys.Control | ModifierKeys.Shift));
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.True(conversionBanner.IsVisible);
                Assert.Equal("변환 완료", conversionBadge.Text);
                Assert.True(openConvertedButton.IsVisible);

                openConvertedButton.Command.Execute(null);
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.Equal(outJpgPath, viewModel.CurrentResult.FilePath);
                Assert.Equal(ImageFormatKind.Jpeg, viewModel.CurrentResult.ImageFormat);
                var reopenedJpg = Assert.IsAssignableFrom<BitmapSource>(previewImage.Source);
                Assert.Equal(120, reopenedJpg.PixelWidth);
                Assert.Equal(80, reopenedJpg.PixelHeight);
                Assert.Contains("JPEG", metadataText.Text, StringComparison.Ordinal);
                Assert.Contains("120 × 80 px", metadataText.Text, StringComparison.Ordinal);

                // 3) JPEG -> PNG 변환 (기본 추천 형식 PNG) 후 결과 파일 다시 열기 확인
                Assert.Equal(ImageFormatKind.Png, viewModel.SelectedConversionFormat);
                convertButton.Command.Execute(convertButton.CommandParameter);
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.Equal("3 / 4", folderPositionText.Text);

                openConvertedButton.Command.Execute(null);
                PumpAndRender(window);

                Assert.True(window.VerifyRenderedState());
                Assert.Equal(outPngPath, viewModel.CurrentResult.FilePath);
                Assert.Equal(ImageFormatKind.Png, viewModel.CurrentResult.ImageFormat);
                var reopenedPng = Assert.IsAssignableFrom<BitmapSource>(previewImage.Source);
                Assert.Equal(120, reopenedPng.PixelWidth);
                Assert.Equal(80, reopenedPng.PixelHeight);
                Assert.Contains("PNG", metadataText.Text, StringComparison.Ordinal);
                Assert.Equal("4 / 4", folderPositionText.Text);

                // 4) PNG -> JPEG 변환 (기본 추천 형식 JPEG) 후 결과 파일 다시 열기 검증
                Assert.Equal(ImageFormatKind.Jpeg, viewModel.SelectedConversionFormat);
                convertButton.Command.Execute(convertButton.CommandParameter);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());

                openConvertedButton.Command.Execute(null);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());
                Assert.Equal(outJpgFromPngPath, viewModel.CurrentResult.FilePath);
                Assert.Equal(ImageFormatKind.Jpeg, viewModel.CurrentResult.ImageFormat);
                var reopenedJpgFromPng = Assert.IsAssignableFrom<BitmapSource>(previewImage.Source);
                Assert.Equal(120, reopenedJpgFromPng.PixelWidth);
                Assert.Equal(80, reopenedJpgFromPng.PixelHeight);

                // 5) JPEG -> BMP 변환 후 결과 파일 다시 열기 검증
                formatCombo.SelectedItem = viewModel.AvailableConversionFormats.Single(o => o.Format == ImageFormatKind.Bmp);
                PumpAndRender(window);
                convertButton.Command.Execute(convertButton.CommandParameter);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());

                openConvertedButton.Command.Execute(null);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());
                Assert.Equal(outBmpFromJpgPath, viewModel.CurrentResult.FilePath);
                Assert.Equal(ImageFormatKind.Bmp, viewModel.CurrentResult.ImageFormat);
                var reopenedBmpFromJpg = Assert.IsAssignableFrom<BitmapSource>(previewImage.Source);
                Assert.Equal(120, reopenedBmpFromJpg.PixelWidth);
                Assert.Equal(80, reopenedBmpFromJpg.PixelHeight);

                // 6) BMP -> PNG 변환 (기본 추천 형식 PNG) 후 결과 파일 다시 열기 검증
                Assert.Equal(ImageFormatKind.Png, viewModel.SelectedConversionFormat);
                convertButton.Command.Execute(convertButton.CommandParameter);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());

                openConvertedButton.Command.Execute(null);
                PumpAndRender(window);
                Assert.True(window.VerifyRenderedState());
                Assert.Equal(outPngFromBmpPath, viewModel.CurrentResult.FilePath);
                Assert.Equal(ImageFormatKind.Png, viewModel.CurrentResult.ImageFormat);
                var reopenedPngFromBmp = Assert.IsAssignableFrom<BitmapSource>(previewImage.Source);
                Assert.Equal(120, reopenedPngFromBmp.PixelWidth);
                Assert.Equal(80, reopenedPngFromBmp.PixelHeight);
                Assert.Equal("7 / 7", folderPositionText.Text);

                // 최초 원본 PNG 파일 바이트가 전체 흐름 이후에도 전혀 변경되지 않았음을 확인합니다.
                Assert.Equal(originalPngBytes, File.ReadAllBytes(sourcePngPath));
            }
            finally
            {
                window.Close();
                FlushDispatcher();
            }
        });
    }

    [Fact]
    public void MainWindow_ImageConversionProtectionAndFailureHandling_PrechecksUnsupportedProtectsOriginalAndCleansFailedOutput()
    {
        using var temp = new TempTestDirectory();
        var gifPath = SampleImageFactory.WriteValidImage(temp.GetPath("01_preview_only.gif"), ImageFormatKind.Gif, 64, 48);
        var tiffPath = SampleImageFactory.WriteValidImage(temp.GetPath("02_preview_only.tiff"), ImageFormatKind.Tiff, 64, 48);
        var webpPath = temp.GetPath("03_unsupported.webp");
        File.WriteAllText(webpPath, "RIFF....WEBP");
        var corruptedPngPath = temp.GetPath("04_corrupted.png");
        File.WriteAllBytes(corruptedPngPath, [0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF]);
        var pngPath = SampleImageFactory.WriteValidImage(temp.GetPath("05_source.png"), ImageFormatKind.Png, 80, 60);
        var originalPngBytes = File.ReadAllBytes(pngPath);
        var existingOtherJpgPath = SampleImageFactory.WriteValidImage(temp.GetPath("06_existing_other.jpg"), ImageFormatKind.Jpeg, 32, 24);
        var existingOtherJpgBytes = File.ReadAllBytes(existingOtherJpgPath);

        var unsupportedTargetGifPath = temp.GetPath("rejected_output.gif");
        var caseVariantSameSourcePath = Path.Combine(temp.DirectoryPath, "05_SOURCE.PNG");
        var midWriteFailureTargetPath = temp.GetPath("failed_output.jpg");
        var silentTruncatedTargetPath = temp.GetPath("silent_truncated_output.jpg");

        var failureMode = 0;
        var conversionService = new ImageConversionService((bitmap, format, stream) =>
        {
            if (failureMode == 1)
            {
                stream.Write([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]);
                stream.Flush();
                throw new IOException("변환 출력 쓰기 중 오류 시뮬레이션");
            }

            if (failureMode == 2)
            {
                stream.Write([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]);
                stream.Flush();
                return;
            }

            BitmapEncoder encoder = format switch
            {
                ImageFormatKind.Jpeg => new JpegBitmapEncoder { QualityLevel = 95 },
                ImageFormatKind.Png => new PngBitmapEncoder(),
                ImageFormatKind.Bmp => new BmpBitmapEncoder(),
                _ => throw new NotSupportedException(),
            };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        });

        StaTestRunner.Run(() =>
        {
            var openDialog = new SequenceFileDialogService();
            var saveDialog = new SequenceSaveFileDialogService(
                unsupportedTargetGifPath,
                caseVariantSameSourcePath,
                existingOtherJpgPath,
                midWriteFailureTargetPath,
                silentTruncatedTargetPath);
            var viewModel = new MainWindowViewModel(
                new ImagePreviewService(),
                openDialog,
                saveDialog,
                new FolderImageNavigationService(),
                conversionService);

            var window = new MainWindow(viewModel);
            try
            {
                ShowWindowForTest(window);

                var convertButton = (Button)window.FindName("ConvertImageButton")!;
                var conversionBanner = (Border)window.FindName("ConversionNoticeBanner")!;
                var conversionBadge = (TextBlock)window.FindName("ConversionNoticeBadgeText")!;
                var conversionMessage = (TextBlock)window.FindName("ConversionNoticeMessageText")!;
                var openConvertedButton = (Button)window.FindName("OpenConvertedFileButton")!;
                var dismissBannerButton = (Button)window.FindName("DismissConversionNoticeButton")!;
                var imageViewport = (Border)window.FindName("ImageViewport")!;
                var noticePanel = (Border)window.FindName("NoticePanel")!;

                // 1) 미지원 원본(GIF, TIFF, WEBP) 및 손상 원본은 저장 대화상자를 열기 전에 사전 차단·안내합니다.
                viewModel.OpenFile(gifPath);
                PumpAndRender(window);
                convertButton.Command.Execute(convertButton.CommandParameter);
                PumpAndRender(window);

                Assert.Equal(0, saveDialog.CallCount);
                Assert.True(window.VerifyRenderedState());
                Assert.True(imageViewport.IsVisible);
                Assert.True(conversionBanner.IsVisible);
                Assert.Equal("변환 미지원", conversionBadge.Text);
                Assert.Contains("GIF", conversionMessage.Text, StringComparison.Ordinal);
                Assert.False(openConvertedButton.IsVisible);

                viewModel.OpenFile(tiffPath);
                PumpAndRender(window);
                Assert.False(conversionBanner.IsVisible);
                convertButton.Command.Execute(convertButton.CommandParameter);
                PumpAndRender(window);

                Assert.Equal(0, saveDialog.CallCount);
                Assert.True(window.VerifyRenderedState());
                Assert.True(conversionBanner.IsVisible);
                Assert.Equal("변환 미지원", conversionBadge.Text);
                Assert.Contains("TIFF", conversionMessage.Text, StringComparison.Ordinal);

                viewModel.OpenFile(webpPath);
                PumpAndRender(window);
                convertButton.Command.Execute(convertButton.CommandParameter);
                PumpAndRender(window);

                Assert.Equal(0, saveDialog.CallCount);
                Assert.True(window.VerifyRenderedState());
                Assert.True(noticePanel.IsVisible);
                Assert.True(conversionBanner.IsVisible);
                Assert.Equal("변환 미지원", conversionBadge.Text);
                Assert.Contains(".webp", conversionMessage.Text, StringComparison.OrdinalIgnoreCase);

                viewModel.OpenFile(corruptedPngPath);
                PumpAndRender(window);
                convertButton.Command.Execute(convertButton.CommandParameter);
                PumpAndRender(window);

                Assert.Equal(0, saveDialog.CallCount);
                Assert.True(window.VerifyRenderedState());
                Assert.True(noticePanel.IsVisible);
                Assert.True(conversionBanner.IsVisible);
                Assert.Equal("변환 실패", conversionBadge.Text);

                // 2) 유효한 PNG에서 미지원 출력 확장자(.gif) 지정 시 파일 생성 전에 차단 및 안내합니다.
                viewModel.OpenFile(pngPath);
                PumpAndRender(window);
                convertButton.Command.Execute(convertButton.CommandParameter);
                PumpAndRender(window);

                Assert.Equal(1, saveDialog.CallCount);
                Assert.True(window.VerifyRenderedState());
                Assert.True(conversionBanner.IsVisible);
                Assert.Equal("변환 미지원", conversionBadge.Text);
                Assert.False(File.Exists(unsupportedTargetGifPath));

                // 3) 원본과 동일한 파일 경로(대소문자 다른 경로 포함) 및 이미 존재하는 다른 파일 덮어쓰기를 차단합니다.
                convertButton.Command.Execute(convertButton.CommandParameter);
                PumpAndRender(window);

                Assert.Equal(2, saveDialog.CallCount);
                Assert.True(window.VerifyRenderedState());
                Assert.True(conversionBanner.IsVisible);
                Assert.Equal("원본 보호", conversionBadge.Text);
                Assert.Equal(originalPngBytes, File.ReadAllBytes(pngPath));

                convertButton.Command.Execute(convertButton.CommandParameter);
                PumpAndRender(window);

                Assert.Equal(3, saveDialog.CallCount);
                Assert.True(window.VerifyRenderedState());
                Assert.True(conversionBanner.IsVisible);
                Assert.Equal("원본 보호", conversionBadge.Text);
                Assert.Equal(existingOtherJpgBytes, File.ReadAllBytes(existingOtherJpgPath));

                // 4) 인코딩 쓰기 도중 예외 발생 또는 잘린 파일 생성 시 불완전 결과 파일을 삭제하고 실패 안내를 표시합니다.
                failureMode = 1;
                convertButton.Command.Execute(convertButton.CommandParameter);
                PumpAndRender(window);

                Assert.Equal(4, saveDialog.CallCount);
                Assert.True(window.VerifyRenderedState());
                Assert.True(imageViewport.IsVisible);
                Assert.True(conversionBanner.IsVisible);
                Assert.Equal("변환 실패", conversionBadge.Text);
                Assert.False(openConvertedButton.IsVisible);
                Assert.False(File.Exists(midWriteFailureTargetPath));
                Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.tmp"));

                failureMode = 2;
                convertButton.Command.Execute(convertButton.CommandParameter);
                PumpAndRender(window);

                Assert.Equal(5, saveDialog.CallCount);
                Assert.True(window.VerifyRenderedState());
                Assert.True(conversionBanner.IsVisible);
                Assert.Equal("변환 실패", conversionBadge.Text);
                Assert.False(openConvertedButton.IsVisible);
                Assert.False(File.Exists(silentTruncatedTargetPath));
                Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.tmp"));

                dismissBannerButton.Command.Execute(null);
                PumpAndRender(window);
                Assert.False(conversionBanner.IsVisible);
                Assert.True(window.VerifyRenderedState());
            }
            finally
            {
                window.Close();
                FlushDispatcher();
            }
        });
    }

    private static void RaisePreviewKeyDown(MainWindow window, Key key)
    {
        var source = PresentationSource.FromVisual(window);
        Assert.NotNull(source);

        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source!, timestamp: 0, key)
        {
            RoutedEvent = UIElement.PreviewKeyDownEvent,
        };
        window.RaiseEvent(args);
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
