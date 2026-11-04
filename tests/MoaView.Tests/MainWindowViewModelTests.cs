using System.IO;
using System.Windows;
using System.Windows.Media;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using MoaView.ViewModels;
using Xunit;

namespace MoaView.Tests;

public sealed class MainWindowViewModelTests
{
    private sealed class StubFileDialogService : IFileDialogService
    {
        public string? NextFilePath { get; set; }

        public string? PickFile(Window? owner = null) => NextFilePath;
    }

    [Fact]
    public void InitialState_ExposesEmptyGuideAndConsistentSupportedFormatText()
    {
        var dialog = new StubFileDialogService();
        var vm = new MainWindowViewModel(new ImagePreviewService(), dialog);

        Assert.Equal(PreviewStateKind.Empty, vm.State);
        Assert.True(vm.IsEmptyState);
        Assert.False(vm.IsImageVisible);
        Assert.False(vm.IsNoticeVisible);
        Assert.Null(vm.PreviewBitmap);
        Assert.Equal("모아뷰 (MoaView)", vm.WindowTitle);
        Assert.Equal(SupportedFileFormats.SupportedFormatsShortText, vm.SupportedFormatsSummary);
        Assert.Equal(SupportedFileFormats.SupportedFormatsDetailText, vm.SupportedFormatsDetail);
        Assert.False(vm.ZoomInCommand.CanExecute(null));
        Assert.False(vm.ZoomOutCommand.CanExecute(null));
        Assert.False(vm.ActualSizeCommand.CanExecute(null));
        Assert.False(vm.ToggleFitToWindowCommand.CanExecute(null));
        Assert.False(vm.RotateClockwiseCommand.CanExecute(null));
        Assert.False(vm.RotateCounterClockwiseCommand.CanExecute(null));
        Assert.False(vm.PreviousImageCommand.CanExecute(null));
        Assert.False(vm.NextImageCommand.CanExecute(null));
    }

    [Fact]
    public void OpenFileCommand_LoadsSelectedSupportedImageAndUpdatesMetadata()
    {
        using var temp = new TempTestDirectory();
        var pngPath = SampleImageFactory.WriteValidImage(
            temp.GetPath("selected.png"),
            ImageFormatKind.Png,
            width: 80,
            height: 50);

        var dialog = new StubFileDialogService { NextFilePath = pngPath };
        var vm = new MainWindowViewModel(new ImagePreviewService(), dialog);

        vm.OpenFileCommand.Execute(null);

        Assert.Equal(PreviewStateKind.ImageLoaded, vm.State);
        Assert.True(vm.IsImageVisible);
        Assert.False(vm.IsEmptyState);
        Assert.False(vm.IsNoticeVisible);
        Assert.NotNull(vm.PreviewBitmap);
        Assert.Equal("selected.png - 모아뷰 (MoaView)", vm.WindowTitle);
        Assert.Contains("PNG", vm.MetadataSummary, StringComparison.Ordinal);
        Assert.Contains("80 × 50 px", vm.MetadataSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectAndOpenFile_WhenDialogCanceled_PreservesCurrentImagePreview()
    {
        using var temp = new TempTestDirectory();
        var jpegPath = SampleImageFactory.WriteValidImage(
            temp.GetPath("keep.jpg"),
            ImageFormatKind.Jpeg,
            width: 40,
            height: 30);

        var dialog = new StubFileDialogService { NextFilePath = jpegPath };
        var vm = new MainWindowViewModel(new ImagePreviewService(), dialog);
        Assert.True(vm.SelectAndOpenFile());

        dialog.NextFilePath = null;
        var opened = vm.SelectAndOpenFile();

        Assert.False(opened);
        Assert.Equal(PreviewStateKind.ImageLoaded, vm.State);
        Assert.NotNull(vm.PreviewBitmap);
        Assert.Equal("keep.jpg - 모아뷰 (MoaView)", vm.WindowTitle);
    }

    [Fact]
    public void OpenFile_SwitchingFromImageToUnsupportedOrCorrupted_ClearsPreviousBitmap()
    {
        using var temp = new TempTestDirectory();
        var validBmp = SampleImageFactory.WriteValidImage(
            temp.GetPath("photo.bmp"),
            ImageFormatKind.Bmp,
            width: 32,
            height: 24);
        var unsupportedWebp = temp.GetPath("next.webp");
        File.WriteAllText(unsupportedWebp, "RIFF....WEBP");
        var corruptedGif = temp.GetPath("broken.gif");
        File.WriteAllText(corruptedGif, "GIF89a-corrupted-stream");

        var vm = new MainWindowViewModel(new ImagePreviewService(), new StubFileDialogService());

        vm.OpenFile(validBmp);
        Assert.True(vm.IsImageVisible);
        Assert.NotNull(vm.PreviewBitmap);

        vm.OpenFile(unsupportedWebp);
        Assert.Equal(PreviewStateKind.UnsupportedFormat, vm.State);
        Assert.False(vm.IsImageVisible);
        Assert.True(vm.IsNoticeVisible);
        Assert.Null(vm.PreviewBitmap);
        Assert.Equal("미지원 형식", vm.NoticeBadgeText);
        Assert.Contains(".webp", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);

        vm.OpenFile(corruptedGif);
        Assert.Equal(PreviewStateKind.Error, vm.State);
        Assert.False(vm.IsImageVisible);
        Assert.True(vm.IsNoticeVisible);
        Assert.Null(vm.PreviewBitmap);
        Assert.Equal("열기 실패", vm.NoticeBadgeText);
    }

    [Fact]
    public void ZoomAndFitCommands_StepAcrossBoundsAndResetToActualSize()
    {
        using var temp = new TempTestDirectory();
        var pngPath = SampleImageFactory.WriteValidImage(
            temp.GetPath("zoom-target.png"),
            ImageFormatKind.Png,
            width: 200,
            height: 100);

        var vm = new MainWindowViewModel(new ImagePreviewService(), new StubFileDialogService());
        vm.OpenFile(pngPath);

        Assert.True(vm.IsFitToWindow);
        Assert.Equal(1.0, vm.ZoomFactor);
        Assert.True(double.IsNaN(vm.ImageDisplayWidth));
        Assert.True(double.IsNaN(vm.ImageDisplayHeight));

        vm.ZoomInCommand.Execute(null);
        Assert.False(vm.IsFitToWindow);
        Assert.Equal(1.25, vm.ZoomFactor);
        Assert.Equal(250.0, vm.ImageDisplayWidth);
        Assert.Equal(125.0, vm.ImageDisplayHeight);
        Assert.Equal("125%", vm.ZoomDisplay);

        vm.ToggleFitToWindowCommand.Execute(null);
        Assert.True(vm.IsFitToWindow);
        Assert.True(double.IsNaN(vm.ImageDisplayWidth));

        vm.ActualSizeCommand.Execute(null);
        Assert.False(vm.IsFitToWindow);
        Assert.Equal(1.0, vm.ZoomFactor);
        Assert.Equal(200.0, vm.ImageDisplayWidth);
        Assert.Equal(100.0, vm.ImageDisplayHeight);
        Assert.Equal("100%", vm.ZoomDisplay);

        while (vm.ZoomOutCommand.CanExecute(null))
        {
            vm.ZoomOutCommand.Execute(null);
        }

        Assert.Equal(MainWindowViewModel.MinZoomFactor, vm.ZoomFactor);
        Assert.False(vm.ZoomOutCommand.CanExecute(null));
        Assert.True(vm.ZoomInCommand.CanExecute(null));

        while (vm.ZoomInCommand.CanExecute(null))
        {
            vm.ZoomInCommand.Execute(null);
        }

        Assert.Equal(MainWindowViewModel.MaxZoomFactor, vm.ZoomFactor);
        Assert.False(vm.ZoomInCommand.CanExecute(null));
        Assert.True(vm.ZoomOutCommand.CanExecute(null));
    }

    [Fact]
    public void RotateCommands_RotatePreviewBitmapQuadrantsAndResetWhenOpeningAnotherFile()
    {
        using var temp = new TempTestDirectory();
        var red = Color.FromRgb(220, 40, 40);
        var green = Color.FromRgb(40, 190, 70);
        var blue = Color.FromRgb(45, 95, 225);
        var yellow = Color.FromRgb(235, 200, 35);

        var firstPath = SampleImageFactory.WriteQuadrantImage(
            temp.GetPath("01_quadrant.png"),
            ImageFormatKind.Png,
            width: 80,
            height: 40,
            topLeft: red,
            topRight: green,
            bottomRight: blue,
            bottomLeft: yellow);
        var secondPath = SampleImageFactory.WriteValidImage(
            temp.GetPath("02_plain.bmp"),
            ImageFormatKind.Bmp,
            width: 60,
            height: 30);

        var vm = new MainWindowViewModel(new ImagePreviewService(), new StubFileDialogService());
        vm.OpenFile(firstPath);

        Assert.Equal(0, vm.RotationDegrees);
        Assert.Equal(80, vm.DisplayPixelWidth);
        Assert.Equal(40, vm.DisplayPixelHeight);

        vm.RotateClockwiseCommand.Execute(null);
        Assert.Equal(90, vm.RotationDegrees);
        Assert.Equal("90°", vm.RotationDisplay);
        Assert.Equal(40, vm.DisplayPixelWidth);
        Assert.Equal(80, vm.DisplayPixelHeight);
        // 원본 메타데이터 해상도는 그대로 유지하고 회전 각도를 함께 표시합니다.
        Assert.Equal(80, vm.CurrentResult.PixelWidth);
        Assert.Equal(40, vm.CurrentResult.PixelHeight);
        Assert.Contains("회전 90°", vm.MetadataSummary, StringComparison.Ordinal);

        // 80x40 -> 시계 방향 90도 회전 시(40x80): 기존 좌상단(red)은 우상단으로 이동합니다.
        Assert.NotNull(vm.PreviewBitmap);
        Assert.Equal(red, SampleImageFactory.ReadPixelColor(vm.PreviewBitmap!, x: 30, y: 10));
        Assert.Equal(green, SampleImageFactory.ReadPixelColor(vm.PreviewBitmap!, x: 30, y: 70));
        Assert.Equal(blue, SampleImageFactory.ReadPixelColor(vm.PreviewBitmap!, x: 10, y: 70));
        Assert.Equal(yellow, SampleImageFactory.ReadPixelColor(vm.PreviewBitmap!, x: 10, y: 10));

        vm.RotateCounterClockwiseCommand.Execute(null);
        Assert.Equal(0, vm.RotationDegrees);
        Assert.Equal(80, vm.DisplayPixelWidth);
        Assert.Equal(40, vm.DisplayPixelHeight);

        vm.RotateCounterClockwiseCommand.Execute(null);
        Assert.Equal(270, vm.RotationDegrees);
        Assert.Equal(40, vm.DisplayPixelWidth);
        Assert.Equal(80, vm.DisplayPixelHeight);
        Assert.Equal(red, SampleImageFactory.ReadPixelColor(vm.PreviewBitmap!, x: 10, y: 70));

        // 다음 파일로 이동하면 회전 및 수동 배율 상태가 기본값으로 초기화되어야 합니다.
        vm.ZoomIn();
        Assert.False(vm.IsFitToWindow);
        vm.NextImageCommand.Execute(null);

        Assert.Equal(secondPath, vm.CurrentResult.FilePath);
        Assert.Equal(0, vm.RotationDegrees);
        Assert.True(vm.IsFitToWindow);
        Assert.Equal(1.0, vm.ZoomFactor);
    }
}
