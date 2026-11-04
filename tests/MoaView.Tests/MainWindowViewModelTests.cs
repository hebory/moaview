using System.IO;
using System.Windows;
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
}
