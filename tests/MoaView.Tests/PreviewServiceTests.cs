using System.IO;
using System.Windows;
using System.Windows.Media;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using Xunit;

namespace MoaView.Tests;

public sealed class PreviewServiceTests
{
    private readonly ImagePreviewService _service = new();

    [Theory]
    [InlineData("sample.jpg", ImageFormatKind.Jpeg, 48, 32)]
    [InlineData("sample.JPEG", ImageFormatKind.Jpeg, 52, 36)]
    [InlineData("sample.png", ImageFormatKind.Png, 64, 40)]
    [InlineData("sample.BMP", ImageFormatKind.Bmp, 30, 22)]
    [InlineData("sample.gif", ImageFormatKind.Gif, 28, 18)]
    [InlineData("sample.tif", ImageFormatKind.Tiff, 44, 26)]
    [InlineData("sample.TIFF", ImageFormatKind.Tiff, 36, 24)]
    public void Load_SupportedImageFormats_DecodesBitmapAndDimensions(
        string fileName,
        ImageFormatKind expectedFormat,
        int expectedWidth,
        int expectedHeight)
    {
        using var temp = new TempTestDirectory();
        var filePath = SampleImageFactory.WriteValidImage(
            temp.GetPath(fileName),
            expectedFormat,
            expectedWidth,
            expectedHeight);

        var result = _service.Load(filePath);

        Assert.Equal(PreviewStateKind.ImageLoaded, result.State);
        Assert.Equal(expectedFormat, result.ImageFormat);
        Assert.Equal(expectedWidth, result.PixelWidth);
        Assert.Equal(expectedHeight, result.PixelHeight);
        Assert.NotNull(result.Bitmap);
        Assert.True(result.Bitmap!.IsFrozen);
        Assert.Equal(expectedWidth, result.Bitmap.PixelWidth);
        Assert.Equal(expectedHeight, result.Bitmap.PixelHeight);
        Assert.True(result.FileSizeBytes > 0);
    }

    [Fact]
    public void Load_LosslessPng_PreservesPixelValuesAndReleasesFileLock()
    {
        using var temp = new TempTestDirectory();
        var expectedColor = Color.FromRgb(24, 148, 212);
        var filePath = SampleImageFactory.WriteValidImage(
            temp.GetPath("color-check.png"),
            ImageFormatKind.Png,
            width: 12,
            height: 8,
            color: expectedColor);

        var result = _service.Load(filePath);

        Assert.Equal(PreviewStateKind.ImageLoaded, result.State);
        Assert.NotNull(result.Bitmap);

        var pixel = new byte[4];
        result.Bitmap!.CopyPixels(new Int32Rect(5, 3, 1, 1), pixel, stride: 4, offset: 0);
        Assert.Equal(expectedColor.B, pixel[0]);
        Assert.Equal(expectedColor.G, pixel[1]);
        Assert.Equal(expectedColor.R, pixel[2]);
        Assert.Equal(255, pixel[3]);

        // 미리보기 로드 이후에도 원본 파일이 잠기지 않고 즉시 삭제 가능해야 합니다.
        File.Delete(filePath);
        Assert.False(File.Exists(filePath));
    }

    [Theory]
    [InlineData("notes.txt", ".txt")]
    [InlineData("modern.webp", ".webp")]
    [InlineData("document.pdf", ".pdf")]
    [InlineData("archive.zip", ".zip")]
    [InlineData("vector.svg", ".svg")]
    [InlineData("icon.ico", ".ico")]
    public void Load_UnsupportedExtension_ReturnsUnsupportedNoticeWithoutBitmap(
        string fileName,
        string expectedExtension)
    {
        using var temp = new TempTestDirectory();
        var filePath = temp.GetPath(fileName);
        File.WriteAllText(filePath, "sample unsupported content");

        var result = _service.Load(filePath);

        Assert.Equal(PreviewStateKind.UnsupportedFormat, result.State);
        Assert.Null(result.Bitmap);
        Assert.Null(result.ImageFormat);
        Assert.Equal(expectedExtension, result.Extension);
        Assert.Contains(expectedExtension, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JPEG", result.Message, StringComparison.Ordinal);
        Assert.Contains("PNG", result.Message, StringComparison.Ordinal);
        Assert.Contains("BMP", result.Message, StringComparison.Ordinal);
        Assert.Contains("GIF", result.Message, StringComparison.Ordinal);
        Assert.Contains("TIFF", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ExtensionlessFile_ReturnsUnsupportedNotice()
    {
        using var temp = new TempTestDirectory();
        var filePath = temp.GetPath("README_WITHOUT_EXTENSION");
        File.WriteAllText(filePath, "plain text");

        var result = _service.Load(filePath);

        Assert.Equal(PreviewStateKind.UnsupportedFormat, result.State);
        Assert.Null(result.Bitmap);
        Assert.Contains("확장자가 없는 파일", result.Message, StringComparison.Ordinal);
        Assert.Contains("JPEG", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_DisguisedIcoContainerWithPngExtension_RejectsUnsupportedContainer()
    {
        using var temp = new TempTestDirectory();
        var disguisedPath = SampleImageFactory.WriteValidIcoFile(temp.GetPath("disguised.png"));

        var result = _service.Load(disguisedPath);

        Assert.Equal(PreviewStateKind.UnsupportedFormat, result.State);
        Assert.Null(result.Bitmap);
        Assert.Null(result.ImageFormat);
        Assert.Contains("지원", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_CorruptedImageData_ReturnsErrorWithoutThrowing()
    {
        using var temp = new TempTestDirectory();
        var corruptedPng = temp.GetPath("corrupted.png");
        File.WriteAllBytes(corruptedPng, [0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF, 0x12, 0x34]);

        var result = _service.Load(corruptedPng);

        Assert.Equal(PreviewStateKind.Error, result.State);
        Assert.Null(result.Bitmap);
        Assert.Contains("손상", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_EmptyFileAndMissingFileAndDirectory_ReturnClearFailureOrUnsupportedNotice()
    {
        using var temp = new TempTestDirectory();

        var emptyBmp = temp.GetPath("empty.bmp");
        File.WriteAllBytes(emptyBmp, []);
        var emptyResult = _service.Load(emptyBmp);
        Assert.Equal(PreviewStateKind.Error, emptyResult.State);
        Assert.Null(emptyResult.Bitmap);
        Assert.Contains("비어", emptyResult.Message, StringComparison.Ordinal);

        var missingResult = _service.Load(temp.GetPath("does-not-exist.jpg"));
        Assert.Equal(PreviewStateKind.Error, missingResult.State);
        Assert.Null(missingResult.Bitmap);
        Assert.Contains("존재하지 않습니다", missingResult.Message, StringComparison.Ordinal);

        var blankPathResult = _service.Load("   ");
        Assert.Equal(PreviewStateKind.Error, blankPathResult.State);
        Assert.Null(blankPathResult.Bitmap);

        var quotedPng = SampleImageFactory.WriteValidImage(
            temp.GetPath("quoted path.png"),
            ImageFormatKind.Png,
            width: 16,
            height: 12);
        var quotedResult = _service.Load($"  \"{quotedPng}\"  ");
        Assert.Equal(PreviewStateKind.ImageLoaded, quotedResult.State);
        Assert.Equal(16, quotedResult.PixelWidth);
        Assert.Equal(12, quotedResult.PixelHeight);

        var folderResult = _service.Load(temp.DirectoryPath);
        Assert.Equal(PreviewStateKind.UnsupportedFormat, folderResult.State);
        Assert.Null(folderResult.Bitmap);
        Assert.Contains("폴더", folderResult.Title, StringComparison.Ordinal);
    }
}
