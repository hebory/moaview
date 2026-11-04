using System.IO;
using System.Windows.Media;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using Xunit;

namespace MoaView.Tests;

public sealed class ImageConversionServiceTests
{
    private readonly ImageConversionService _conversionService = new();
    private readonly ImagePreviewService _previewService = new();

    [Theory]
    [InlineData(ImageFormatKind.Jpeg, ".jpg", ImageFormatKind.Png, ".png", false)]
    [InlineData(ImageFormatKind.Jpeg, ".jpeg", ImageFormatKind.Bmp, ".bmp", false)]
    [InlineData(ImageFormatKind.Jpeg, ".jpeg", ImageFormatKind.Jpeg, ".jpg", false)]
    [InlineData(ImageFormatKind.Png, ".png", ImageFormatKind.Jpeg, ".jpg", false)]
    [InlineData(ImageFormatKind.Png, ".png", ImageFormatKind.Bmp, ".bmp", true)]
    [InlineData(ImageFormatKind.Png, ".png", ImageFormatKind.Png, ".png", true)]
    [InlineData(ImageFormatKind.Bmp, ".bmp", ImageFormatKind.Jpeg, ".jpeg", false)]
    [InlineData(ImageFormatKind.Bmp, ".bmp", ImageFormatKind.Png, ".png", true)]
    [InlineData(ImageFormatKind.Bmp, ".bmp", ImageFormatKind.Bmp, ".bmp", true)]
    public void Convert_AllSupportedJpegPngBmpCombinations_ReopensWithExactDimensionsActualFormatAndColors(
        ImageFormatKind sourceFormat,
        string sourceExtension,
        ImageFormatKind targetFormat,
        string targetExtension,
        bool expectLosslessPixels)
    {
        using var temp = new TempTestDirectory();
        var topLeft = Color.FromRgb(215, 45, 50);
        var topRight = Color.FromRgb(40, 185, 75);
        var bottomRight = Color.FromRgb(45, 95, 220);
        var bottomLeft = Color.FromRgb(230, 195, 40);

        var sourcePath = SampleImageFactory.WriteQuadrantImage(
            temp.GetPath($"source_image{sourceExtension}"),
            sourceFormat,
            width: 96,
            height: 64,
            topLeft: topLeft,
            topRight: topRight,
            bottomRight: bottomRight,
            bottomLeft: bottomLeft);
        var originalSourceBytes = File.ReadAllBytes(sourcePath);

        var outputPath = temp.GetPath($"converted_output{targetExtension}");
        var conversion = _conversionService.Convert(sourcePath, targetFormat, outputPath);

        Assert.True(conversion.IsSuccess);
        Assert.Equal(ConversionStatusKind.Succeeded, conversion.Status);
        Assert.Equal(sourceFormat, conversion.SourceFormat);
        Assert.Equal(targetFormat, conversion.TargetFormat);
        Assert.Equal(96, conversion.PixelWidth);
        Assert.Equal(64, conversion.PixelHeight);
        Assert.True(conversion.OutputFileSizeBytes > 0);
        Assert.True(File.Exists(outputPath));

        // 원본 파일 바이트가 1바이트도 변경되지 않았는지 확인합니다.
        Assert.Equal(originalSourceBytes, File.ReadAllBytes(sourcePath));

        // 변환 결과 파일을 실제 미리보기 디코더로 다시 열어 출력 컨테이너 형식과 픽셀 크기를 검증합니다.
        var reopened = _previewService.Load(outputPath);
        Assert.Equal(PreviewStateKind.ImageLoaded, reopened.State);
        Assert.Equal(targetFormat, reopened.ImageFormat);
        Assert.Equal(96, reopened.PixelWidth);
        Assert.Equal(64, reopened.PixelHeight);
        Assert.NotNull(reopened.Bitmap);

        var tolerance = expectLosslessPixels ? 0 : 12;
        AssertColorClose(topLeft, SampleImageFactory.ReadPixelColor(reopened.Bitmap!, x: 20, y: 15), tolerance);
        AssertColorClose(topRight, SampleImageFactory.ReadPixelColor(reopened.Bitmap!, x: 75, y: 15), tolerance);
        AssertColorClose(bottomRight, SampleImageFactory.ReadPixelColor(reopened.Bitmap!, x: 75, y: 50), tolerance);
        AssertColorClose(bottomLeft, SampleImageFactory.ReadPixelColor(reopened.Bitmap!, x: 20, y: 50), tolerance);

        // 파일 핸들 잠금이 남지 않아 즉시 삭제 가능한지 검증합니다.
        File.Delete(outputPath);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void Convert_UnsupportedSourceFormatsAndDisguisedContainers_RejectBeforeCreatingOutputFile()
    {
        using var temp = new TempTestDirectory();

        var gifSource = SampleImageFactory.WriteValidImage(temp.GetPath("anim.gif"), ImageFormatKind.Gif, 40, 30);
        var tiffSource = SampleImageFactory.WriteValidImage(temp.GetPath("scan.tiff"), ImageFormatKind.Tiff, 40, 30);
        var webpSource = temp.GetPath("modern.webp");
        File.WriteAllText(webpSource, "RIFF....WEBP");
        var extensionlessSource = temp.GetPath("RAW_FILE");
        File.WriteAllText(extensionlessSource, "plain data");

        // 확장자는 .png / .jpg / .bmp이지만 실제 컨테이너는 GIF / TIFF / ICO인 위장 파일
        var disguisedGifAsPng = SampleImageFactory.WriteValidImage(temp.GetPath("disguised_gif.png"), ImageFormatKind.Gif, 24, 24);
        var disguisedTiffAsJpg = SampleImageFactory.WriteValidImage(temp.GetPath("disguised_tiff.jpg"), ImageFormatKind.Tiff, 24, 24);
        var disguisedIcoAsBmp = SampleImageFactory.WriteValidIcoFile(temp.GetPath("disguised_ico.bmp"));

        string[] unsupportedSources =
        [
            gifSource,
            tiffSource,
            webpSource,
            extensionlessSource,
            disguisedGifAsPng,
            disguisedTiffAsJpg,
            disguisedIcoAsBmp,
            temp.DirectoryPath,
        ];

        for (var i = 0; i < unsupportedSources.Length; i++)
        {
            var outputPath = temp.GetPath($"should_not_exist_{i}.png");
            var result = _conversionService.Convert(unsupportedSources[i], ImageFormatKind.Png, outputPath);

            Assert.Equal(ConversionStatusKind.UnsupportedCombination, result.Status);
            Assert.False(result.IsSuccess);
            Assert.False(File.Exists(outputPath));
            Assert.Contains("JPEG", result.Message, StringComparison.Ordinal);
            Assert.Contains("PNG", result.Message, StringComparison.Ordinal);
            Assert.Contains("BMP", result.Message, StringComparison.Ordinal);
        }

        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.tmp"));
    }

    [Theory]
    [InlineData(ImageFormatKind.Gif, "out.gif")]
    [InlineData(ImageFormatKind.Tiff, "out.tiff")]
    [InlineData(ImageFormatKind.Png, "out.gif")]
    [InlineData(ImageFormatKind.Png, "out.tif")]
    [InlineData(ImageFormatKind.Png, "out.webp")]
    [InlineData(ImageFormatKind.Png, "out_no_extension")]
    [InlineData(ImageFormatKind.Png, "mismatch.bmp")]
    [InlineData(ImageFormatKind.Jpeg, "mismatch.png")]
    public void Convert_UnsupportedTargetFormatOrMismatchedOutputExtension_RejectsBeforeCreatingOutputFile(
        ImageFormatKind requestedTargetFormat,
        string outputFileName)
    {
        using var temp = new TempTestDirectory();
        var validPng = SampleImageFactory.WriteValidImage(
            temp.GetPath("valid_source.png"),
            ImageFormatKind.Png,
            width: 32,
            height: 24);
        var outputPath = temp.GetPath(outputFileName);

        var result = _conversionService.Convert(validPng, requestedTargetFormat, outputPath);

        Assert.Equal(ConversionStatusKind.UnsupportedCombination, result.Status);
        Assert.False(result.IsSuccess);
        Assert.False(File.Exists(outputPath));
        Assert.Contains("JPEG", result.Message, StringComparison.Ordinal);
        Assert.Contains("PNG", result.Message, StringComparison.Ordinal);
        Assert.Contains("BMP", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_SamePathCaseVariantRelativePathOrExistingFile_ProtectsOriginalAndExistingFiles()
    {
        using var temp = new TempTestDirectory();
        var originalPng = SampleImageFactory.WriteValidImage(
            temp.GetPath("protected.png"),
            ImageFormatKind.Png,
            width: 48,
            height: 36,
            color: Color.FromRgb(20, 120, 210));
        var originalPngBytes = File.ReadAllBytes(originalPng);

        var existingOtherBmp = SampleImageFactory.WriteValidImage(
            temp.GetPath("already_exists.bmp"),
            ImageFormatKind.Bmp,
            width: 16,
            height: 16,
            color: Color.FromRgb(200, 60, 40));
        var existingOtherBytes = File.ReadAllBytes(existingOtherBmp);

        var upperCaseSamePath = Path.Combine(temp.DirectoryPath, "PROTECTED.PNG");
        var relativeSamePath = Path.Combine(temp.DirectoryPath, ".", "protected.png");
        var quotedSamePath = $"\"{originalPng}\"";

        foreach (var candidateSamePath in new[] { originalPng, upperCaseSamePath, relativeSamePath, quotedSamePath })
        {
            var blocked = _conversionService.Convert(originalPng, ImageFormatKind.Png, candidateSamePath);
            Assert.Equal(ConversionStatusKind.OriginalOverwriteBlocked, blocked.Status);
            Assert.False(blocked.IsSuccess);
            Assert.Equal(originalPngBytes, File.ReadAllBytes(originalPng));
        }

        // 확장자가 달라도 원본 경로 자체를 대상으로 지정한 경우 원본 보호가 최우선 동작해야 합니다.
        var wrongFormatSamePath = _conversionService.Convert(originalPng, ImageFormatKind.Jpeg, originalPng);
        Assert.Equal(ConversionStatusKind.OriginalOverwriteBlocked, wrongFormatSamePath.Status);
        Assert.Equal(originalPngBytes, File.ReadAllBytes(originalPng));

        // 이미 존재하는 다른 파일 경로로도 덮어쓰지 않고 기존 파일을 보존합니다.
        var existingBlocked = _conversionService.Convert(originalPng, ImageFormatKind.Bmp, existingOtherBmp);
        Assert.Equal(ConversionStatusKind.OriginalOverwriteBlocked, existingBlocked.Status);
        Assert.Equal(existingOtherBytes, File.ReadAllBytes(existingOtherBmp));

        // 추천 경로 생성기는 원본 및 기존 파일과 충돌하지 않는 새 경로를 반환합니다.
        var suggestedSameFormat = _conversionService.BuildSuggestedOutputPath(originalPng, ImageFormatKind.Png);
        Assert.NotEqual(originalPng, suggestedSameFormat, StringComparer.OrdinalIgnoreCase);
        Assert.EndsWith("protected_converted.png", suggestedSameFormat, StringComparison.OrdinalIgnoreCase);

        File.WriteAllBytes(suggestedSameFormat, [1, 2, 3]);
        var suggestedNext = _conversionService.BuildSuggestedOutputPath(originalPng, ImageFormatKind.Png);
        Assert.EndsWith("protected_converted_2.png", suggestedNext, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Convert_WhenWriteFailsMidStreamOrSourceIsCorrupted_CleansIncompleteArtifactsAndLeavesNoOutput()
    {
        using var temp = new TempTestDirectory();
        var validJpg = SampleImageFactory.WriteValidImage(
            temp.GetPath("clean_source.jpg"),
            ImageFormatKind.Jpeg,
            width: 50,
            height: 40);

        // 1) 임시 파일에 불완전 헤더 바이트가 이미 기록된 상태에서 IOException 발생 시 정리 검증
        var failingMidWriteService = new ImageConversionService((_, _, stream) =>
        {
            stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            stream.Flush();
            throw new IOException("디스크 쓰기 중단 시뮬레이션");
        });

        var failedOutputPath = temp.GetPath("must_not_remain.png");
        var midWriteFailure = failingMidWriteService.Convert(validJpg, ImageFormatKind.Png, failedOutputPath);

        Assert.Equal(ConversionStatusKind.Failed, midWriteFailure.Status);
        Assert.False(midWriteFailure.IsSuccess);
        Assert.False(File.Exists(failedOutputPath));
        Assert.Equal([validJpg], Directory.GetFiles(temp.DirectoryPath));

        // 2) 인코더가 0바이트만 남기고 종료한 비정상 상태에서도 빈 결과 파일이 남지 않는지 검증
        var zeroByteWriteService = new ImageConversionService((_, _, _) => { });
        var emptyAttemptPath = temp.GetPath("must_not_be_zero_byte.bmp");
        var zeroByteFailure = zeroByteWriteService.Convert(validJpg, ImageFormatKind.Bmp, emptyAttemptPath);

        Assert.Equal(ConversionStatusKind.Failed, zeroByteFailure.Status);
        Assert.False(File.Exists(emptyAttemptPath));
        Assert.Equal([validJpg], Directory.GetFiles(temp.DirectoryPath));

        // 3) 예외 없이 잘린/손상된 바이트만 기록한 경우에도 성공으로 오인하지 않고 산출물을 즉시 제거하는지 검증
        var silentTruncatedWriteService = new ImageConversionService((_, _, stream) =>
        {
            stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00]);
            stream.Flush();
        });
        var truncatedAttemptPath = temp.GetPath("must_not_look_succeeded.png");
        var truncatedFailure = silentTruncatedWriteService.Convert(validJpg, ImageFormatKind.Png, truncatedAttemptPath);

        Assert.Equal(ConversionStatusKind.Failed, truncatedFailure.Status);
        Assert.False(truncatedFailure.IsSuccess);
        Assert.False(File.Exists(truncatedAttemptPath));
        Assert.Equal([validJpg], Directory.GetFiles(temp.DirectoryPath));

        // 4) 손상된 원본 이미지, 빈 원본, 존재하지 않는 원본, 존재하지 않는 대상 폴더 검증
        var corruptedPng = temp.GetPath("corrupted_source.png");
        File.WriteAllBytes(corruptedPng, [0x89, 0x50, 0x4E, 0x47, 0x00, 0x99]);
        var corruptedOut = temp.GetPath("from_corrupted.jpg");
        var corruptedResult = _conversionService.Convert(corruptedPng, ImageFormatKind.Jpeg, corruptedOut);
        Assert.Equal(ConversionStatusKind.Failed, corruptedResult.Status);
        Assert.False(File.Exists(corruptedOut));

        var emptySource = temp.GetPath("empty_source.bmp");
        File.WriteAllBytes(emptySource, []);
        var emptyOut = temp.GetPath("from_empty.png");
        var emptyResult = _conversionService.Convert(emptySource, ImageFormatKind.Png, emptyOut);
        Assert.Equal(ConversionStatusKind.Failed, emptyResult.Status);
        Assert.False(File.Exists(emptyOut));

        var missingOut = temp.GetPath("from_missing.png");
        var missingResult = _conversionService.Convert(temp.GetPath("no_such_file.jpg"), ImageFormatKind.Png, missingOut);
        Assert.Equal(ConversionStatusKind.Failed, missingResult.Status);
        Assert.False(File.Exists(missingOut));

        var missingDirOut = Path.Combine(temp.DirectoryPath, "missing_folder", "result.png");
        var missingDirResult = _conversionService.Convert(validJpg, ImageFormatKind.Png, missingDirOut);
        Assert.Equal(ConversionStatusKind.Failed, missingDirResult.Status);
        Assert.False(File.Exists(missingDirOut));
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath, "*.tmp"));
    }

    private static void AssertColorClose(Color expected, Color actual, int maxChannelDelta)
    {
        Assert.InRange(Math.Abs(expected.R - actual.R), 0, maxChannelDelta);
        Assert.InRange(Math.Abs(expected.G - actual.G), 0, maxChannelDelta);
        Assert.InRange(Math.Abs(expected.B - actual.B), 0, maxChannelDelta);
    }
}
