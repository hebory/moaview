using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MoaView.Models;

namespace MoaView.Services;

/// <summary>
/// JPEG, PNG, BMP 상호 변환을 수행하며 원본 덮어쓰기 방지, 미지원 조합 사전 안내,
/// 실패 시 불완전 산출물 정리를 보장합니다.
/// </summary>
public sealed class ImageConversionService : IImageConversionService
{
    public const int DefaultJpegQualityLevel = 95;

    private readonly Action<BitmapSource, ImageFormatKind, Stream> _encodeToStream;

    public ImageConversionService()
        : this(DefaultEncodeToStream)
    {
    }

    public ImageConversionService(Action<BitmapSource, ImageFormatKind, Stream> encodeToStream)
    {
        ArgumentNullException.ThrowIfNull(encodeToStream);
        _encodeToStream = encodeToStream;
    }

    public ImageConversionResult ValidateCanConvertSource(
        string? sourceFilePath,
        ImageFormatKind? loadedSourceFormat = null)
    {
        var sanitizedSource = SanitizePath(sourceFilePath);
        if (string.IsNullOrWhiteSpace(sanitizedSource))
        {
            return ImageConversionResult.Failure(
                sourceFilePath,
                outputFilePath: null,
                sourceFormat: loadedSourceFormat,
                targetFormat: null,
                title: "변환할 파일이 없습니다.",
                message: "변환할 원본 이미지 파일을 먼저 선택해 주세요.");
        }

        if (!TryGetFullPath(sanitizedSource, out var sourceFullPath))
        {
            return ImageConversionResult.Failure(
                sourceFilePath,
                outputFilePath: null,
                sourceFormat: loadedSourceFormat,
                targetFormat: null,
                title: "원본 파일 경로가 올바르지 않습니다.",
                message: "변환할 원본 파일 경로 형식을 해석할 수 없습니다.");
        }

        if (Directory.Exists(sourceFullPath))
        {
            return ImageConversionResult.Unsupported(
                sourceFullPath,
                outputFilePath: null,
                sourceFormat: null,
                targetFormat: null,
                title: "폴더는 이미지 변환을 지원하지 않습니다.",
                message: $"폴더가 아닌 지원 이미지 파일을 선택해 주세요. {SupportedFileFormats.SupportedConversionDetailText}.");
        }

        if (loadedSourceFormat is { } knownFormat && !SupportedFileFormats.IsConvertibleFormat(knownFormat))
        {
            return ImageConversionResult.Unsupported(
                sourceFullPath,
                outputFilePath: null,
                sourceFormat: knownFormat,
                targetFormat: null,
                title: "지원하지 않는 변환 원본 형식입니다.",
                message: SupportedFileFormats.BuildUnsupportedConversionSourceMessage(knownFormat));
        }

        var sourceExtension = Path.GetExtension(sourceFullPath);
        if (!SupportedFileFormats.TryGetConvertibleFormatByExtension(sourceExtension, out var extFormat))
        {
            SupportedFileFormats.TryGetImageFormatByExtension(sourceExtension, out var detectedPreviewFormat);
            ImageFormatKind? reportedFormat = SupportedFileFormats.TryGetImageFormatByExtension(sourceExtension, out _)
                ? detectedPreviewFormat
                : loadedSourceFormat;

            return ImageConversionResult.Unsupported(
                sourceFullPath,
                outputFilePath: null,
                sourceFormat: reportedFormat,
                targetFormat: null,
                title: "지원하지 않는 변환 원본 형식입니다.",
                message: SupportedFileFormats.BuildUnsupportedConversionSourceExtensionMessage(sourceExtension));
        }

        if (!File.Exists(sourceFullPath))
        {
            return ImageConversionResult.Failure(
                sourceFullPath,
                outputFilePath: null,
                sourceFormat: loadedSourceFormat ?? extFormat,
                targetFormat: null,
                title: "원본 파일을 찾을 수 없습니다.",
                message: $"변환할 원본 파일이 존재하지 않습니다: {sourceFullPath}");
        }

        return ImageConversionResult.Ready(sourceFullPath, loadedSourceFormat ?? extFormat);
    }

    public string BuildSuggestedOutputPath(string sourceFilePath, ImageFormatKind targetFormat)
    {
        var defaultExtension = SupportedFileFormats.IsConvertibleFormat(targetFormat)
            ? SupportedFileFormats.GetDefaultExtension(targetFormat)
            : ".png";

        var sanitized = SanitizePath(sourceFilePath);
        if (string.IsNullOrWhiteSpace(sanitized) || !TryGetFullPath(sanitized, out var sourceFullPath))
        {
            return $"converted{defaultExtension}";
        }

        var directory = Path.GetDirectoryName(sourceFullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Environment.CurrentDirectory;
        }

        var stem = Path.GetFileNameWithoutExtension(sourceFullPath);
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "converted";
        }

        var directCandidate = Path.Combine(directory, $"{stem}{defaultExtension}");
        if (!IsSameFileOrLinkTarget(sourceFullPath, directCandidate) && !File.Exists(directCandidate))
        {
            return directCandidate;
        }

        var convertedCandidate = Path.Combine(directory, $"{stem}_converted{defaultExtension}");
        if (!IsSameFileOrLinkTarget(sourceFullPath, convertedCandidate) && !File.Exists(convertedCandidate))
        {
            return convertedCandidate;
        }

        for (var index = 2; index < 10_000; index++)
        {
            var numberedCandidate = Path.Combine(
                directory,
                string.Create(CultureInfo.InvariantCulture, $"{stem}_converted_{index}{defaultExtension}"));
            if (!IsSameFileOrLinkTarget(sourceFullPath, numberedCandidate) && !File.Exists(numberedCandidate))
            {
                return numberedCandidate;
            }
        }

        return Path.Combine(directory, $"{stem}_{Guid.NewGuid():N}{defaultExtension}");
    }

    public ImageConversionResult Convert(
        string? sourceFilePath,
        ImageFormatKind targetFormat,
        string? destinationFilePath)
    {
        if (!SupportedFileFormats.IsConvertibleFormat(targetFormat))
        {
            return ImageConversionResult.Unsupported(
                sourceFilePath,
                destinationFilePath,
                sourceFormat: null,
                targetFormat: targetFormat,
                title: "지원하지 않는 변환 출력 형식입니다.",
                message: SupportedFileFormats.BuildUnsupportedConversionTargetMessage(
                    SupportedFileFormats.GetDisplayName(targetFormat)));
        }

        var precheck = ValidateCanConvertSource(sourceFilePath);
        if (!precheck.IsSuccess)
        {
            return precheck with { TargetFormat = targetFormat, OutputFilePath = destinationFilePath };
        }

        var sourceFullPath = precheck.SourceFilePath!;

        var sanitizedDestination = SanitizePath(destinationFilePath);
        if (string.IsNullOrWhiteSpace(sanitizedDestination))
        {
            return ImageConversionResult.Failure(
                sourceFullPath,
                destinationFilePath,
                precheck.SourceFormat,
                targetFormat,
                title: "저장 경로가 지정되지 않았습니다.",
                message: "변환된 이미지를 저장할 파일 경로를 지정해 주세요.");
        }

        if (!TryGetFullPath(sanitizedDestination, out var destFullPath))
        {
            return ImageConversionResult.Failure(
                sourceFullPath,
                destinationFilePath,
                precheck.SourceFormat,
                targetFormat,
                title: "저장 경로가 올바르지 않습니다.",
                message: "지정한 출력 파일 경로 형식을 해석할 수 없습니다.");
        }

        if (Directory.Exists(destFullPath) || string.IsNullOrWhiteSpace(Path.GetFileName(destFullPath)))
        {
            return ImageConversionResult.Failure(
                sourceFullPath,
                destFullPath,
                precheck.SourceFormat,
                targetFormat,
                title: "저장 경로가 올바르지 않습니다.",
                message: "폴더가 아닌 출력 이미지 파일 이름을 지정해 주세요.");
        }

        // 원본과 동일한 경로(대소문자/상대경로/심볼릭 링크 포함)는 확장자 검사와 무관하게 최우선으로 차단합니다.
        if (IsSameFileOrLinkTarget(sourceFullPath, destFullPath))
        {
            return ImageConversionResult.OverwriteBlocked(
                sourceFullPath,
                destFullPath,
                precheck.SourceFormat,
                targetFormat,
                title: "원본 파일을 덮어쓸 수 없습니다.",
                message: "원본 이미지 보호를 위해 원본과 동일한 파일 경로로는 변환 저장할 수 없습니다. 다른 파일 이름으로 저장해 주세요.");
        }

        var destExtension = Path.GetExtension(destFullPath);
        if (!SupportedFileFormats.TryGetConvertibleFormatByExtension(destExtension, out var destExtFormat))
        {
            var extDescription = string.IsNullOrWhiteSpace(destExtension) ? "확장자 없음" : destExtension;
            return ImageConversionResult.Unsupported(
                sourceFullPath,
                destFullPath,
                precheck.SourceFormat,
                targetFormat,
                title: "지원하지 않는 변환 출력 형식입니다.",
                message: SupportedFileFormats.BuildUnsupportedConversionTargetMessage(extDescription));
        }

        if (destExtFormat != targetFormat)
        {
            return ImageConversionResult.Unsupported(
                sourceFullPath,
                destFullPath,
                precheck.SourceFormat,
                targetFormat,
                title: "변환 형식과 출력 확장자가 일치하지 않습니다.",
                message: $"선택한 변환 형식({SupportedFileFormats.GetDisplayName(targetFormat)})과 출력 파일 확장자('{destExtension}')가 일치하지 않습니다. {SupportedFileFormats.SupportedConversionDetailText}.");
        }

        if (File.Exists(destFullPath))
        {
            return ImageConversionResult.OverwriteBlocked(
                sourceFullPath,
                destFullPath,
                precheck.SourceFormat,
                targetFormat,
                title: "이미 존재하는 파일을 덮어쓸 수 없습니다.",
                message: "지정한 경로에 이미 파일이 존재하여 기존 파일을 보호했습니다. 다른 파일 이름으로 저장해 주세요.");
        }

        var destDirectory = Path.GetDirectoryName(destFullPath);
        if (string.IsNullOrWhiteSpace(destDirectory) || !Directory.Exists(destDirectory))
        {
            return ImageConversionResult.Failure(
                sourceFullPath,
                destFullPath,
                precheck.SourceFormat,
                targetFormat,
                title: "저장 폴더를 찾을 수 없습니다.",
                message: "변환 결과를 저장할 대상 폴더가 존재하지 않습니다.");
        }

        var decodeOutcome = TryDecodeConvertibleSource(
            sourceFullPath,
            destFullPath,
            targetFormat,
            out var actualSourceFormat,
            out var decodedBitmap);
        if (decodeOutcome is not null || decodedBitmap is null)
        {
            return decodeOutcome ?? ImageConversionResult.Failure(
                sourceFullPath,
                destFullPath,
                precheck.SourceFormat,
                targetFormat,
                title: "원본 이미지를 읽을 수 없습니다.",
                message: "원본 이미지 디코딩에 실패하여 변환을 중단했습니다.");
        }

        var tempFilePath = Path.Combine(destDirectory, $".moaview-convert-{Guid.NewGuid():N}.tmp");
        var movedToDestination = false;

        try
        {
            using (var tempStream = new FileStream(
                tempFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                _encodeToStream(decodedBitmap, targetFormat, tempStream);
                tempStream.Flush(flushToDisk: true);
            }

            var tempInfo = new FileInfo(tempFilePath);
            if (!tempInfo.Exists || tempInfo.Length <= 0)
            {
                throw new IOException("변환된 이미지 데이터가 비어 있습니다.");
            }

            VerifyEncodedTempFile(
                tempFilePath,
                targetFormat,
                decodedBitmap.PixelWidth,
                decodedBitmap.PixelHeight);

            File.Move(tempFilePath, destFullPath, overwrite: false);
            movedToDestination = true;

            var outputInfo = new FileInfo(destFullPath);
            var outputFileName = Path.GetFileName(destFullPath);
            var targetDisplayName = SupportedFileFormats.GetDisplayName(targetFormat);
            var sourceDisplayName = SupportedFileFormats.GetDisplayName(actualSourceFormat);

            return ImageConversionResult.Success(
                sourceFullPath,
                destFullPath,
                actualSourceFormat,
                targetFormat,
                decodedBitmap.PixelWidth,
                decodedBitmap.PixelHeight,
                outputInfo.Length,
                title: $"{targetDisplayName} 변환 완료",
                message: $"{sourceDisplayName} 이미지를 {targetDisplayName} 형식으로 변환 저장했습니다: {outputFileName} ({decodedBitmap.PixelWidth} × {decodedBitmap.PixelHeight} px)");
        }
        catch (UnauthorizedAccessException)
        {
            CleanupFailedArtifacts(tempFilePath, movedToDestination ? destFullPath : null);
            return ImageConversionResult.Failure(
                sourceFullPath,
                destFullPath,
                actualSourceFormat,
                targetFormat,
                title: "파일 저장 권한이 없습니다.",
                message: "대상 폴더에 쓰기 권한이 없어 변환을 완료하지 못했습니다. 불완전한 결과 파일은 생성되지 않았습니다.");
        }
        catch (IOException)
        {
            CleanupFailedArtifacts(tempFilePath, movedToDestination ? destFullPath : null);
            return ImageConversionResult.Failure(
                sourceFullPath,
                destFullPath,
                actualSourceFormat,
                targetFormat,
                title: "이미지 변환 저장에 실패했습니다.",
                message: "파일 쓰기 중 입출력 오류가 발생해 변환을 중단하고 불완전한 산출물을 정리했습니다.");
        }
        catch (Exception ex) when (
            ex is NotSupportedException
            or FileFormatException
            or InvalidOperationException
            or ArgumentException
            or OverflowException
            or COMException
            or System.Security.SecurityException)
        {
            CleanupFailedArtifacts(tempFilePath, movedToDestination ? destFullPath : null);
            return ImageConversionResult.Failure(
                sourceFullPath,
                destFullPath,
                actualSourceFormat,
                targetFormat,
                title: "이미지 인코딩에 실패했습니다.",
                message: "선택한 형식으로 인코딩하는 중 오류가 발생해 불완전한 산출물을 정리했습니다.");
        }
        finally
        {
            if (!movedToDestination)
            {
                TryDeleteFile(tempFilePath);
            }
        }
    }

    private static ImageConversionResult? TryDecodeConvertibleSource(
        string sourceFullPath,
        string destFullPath,
        ImageFormatKind targetFormat,
        out ImageFormatKind actualSourceFormat,
        out BitmapSource? detachedBitmap)
    {
        actualSourceFormat = default;
        detachedBitmap = null;

        try
        {
            using var fileStream = new FileStream(
                sourceFullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var fileLength = fileStream.Length;
            if (fileLength <= 0)
            {
                return ImageConversionResult.Failure(
                    sourceFullPath,
                    destFullPath,
                    sourceFormat: null,
                    targetFormat: targetFormat,
                    title: "원본 이미지가 비어 있습니다.",
                    message: "원본 파일이 비어 있어 변환할 수 없습니다.");
            }

            using var memoryStream = new MemoryStream((int)Math.Min(fileLength, 64 * 1024 * 1024));
            fileStream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            var decoder = BitmapDecoder.Create(
                memoryStream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);

            if (!TryGetDecoderFormat(decoder, out var detectedFormat))
            {
                return ImageConversionResult.Unsupported(
                    sourceFullPath,
                    destFullPath,
                    sourceFormat: null,
                    targetFormat: targetFormat,
                    title: "지원하지 않는 변환 원본 형식입니다.",
                    message: $"파일 확장자와 달리 실제 데이터 형식이 변환 지원 범위 밖입니다. {SupportedFileFormats.SupportedConversionDetailText}.");
            }

            actualSourceFormat = detectedFormat;
            if (!SupportedFileFormats.IsConvertibleFormat(detectedFormat))
            {
                return ImageConversionResult.Unsupported(
                    sourceFullPath,
                    destFullPath,
                    sourceFormat: detectedFormat,
                    targetFormat: targetFormat,
                    title: "지원하지 않는 변환 원본 형식입니다.",
                    message: SupportedFileFormats.BuildUnsupportedConversionSourceMessage(detectedFormat));
            }

            if (decoder.Frames.Count == 0)
            {
                return ImageConversionResult.Failure(
                    sourceFullPath,
                    destFullPath,
                    actualSourceFormat,
                    targetFormat,
                    title: "원본 이미지를 읽을 수 없습니다.",
                    message: "원본 파일에서 이미지 프레임을 찾을 수 없습니다.");
            }

            var firstFrame = decoder.Frames[0];
            var width = firstFrame.PixelWidth;
            var height = firstFrame.PixelHeight;
            if (width <= 0 || height <= 0)
            {
                return ImageConversionResult.Failure(
                    sourceFullPath,
                    destFullPath,
                    actualSourceFormat,
                    targetFormat,
                    title: "원본 이미지 크기가 올바르지 않습니다.",
                    message: "유효하지 않은 원본 이미지 크기여서 변환할 수 없습니다.");
            }

            BitmapSource bgraSource = firstFrame.Format == PixelFormats.Bgra32
                ? firstFrame
                : new FormatConvertedBitmap(firstFrame, PixelFormats.Bgra32, null, 0);

            var stride = checked(width * 4);
            var pixels = new byte[checked(height * stride)];
            bgraSource.CopyPixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);

            var dpiX = double.IsFinite(firstFrame.DpiX) && firstFrame.DpiX > 0 ? firstFrame.DpiX : 96.0;
            var dpiY = double.IsFinite(firstFrame.DpiY) && firstFrame.DpiY > 0 ? firstFrame.DpiY : 96.0;

            var cleanBitmap = BitmapSource.Create(
                width,
                height,
                dpiX,
                dpiY,
                PixelFormats.Bgra32,
                palette: null,
                pixels,
                stride);
            cleanBitmap.Freeze();
            detachedBitmap = cleanBitmap;
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return ImageConversionResult.Failure(
                sourceFullPath,
                destFullPath,
                sourceFormat: null,
                targetFormat: targetFormat,
                title: "원본 파일에 접근할 수 없습니다.",
                message: "원본 파일 읽기 권한이 없어 변환할 수 없습니다.");
        }
        catch (IOException)
        {
            return ImageConversionResult.Failure(
                sourceFullPath,
                destFullPath,
                sourceFormat: null,
                targetFormat: targetFormat,
                title: "원본 파일을 읽을 수 없습니다.",
                message: "원본 파일 입출력 오류가 발생해 변환할 수 없습니다.");
        }
        catch (Exception ex) when (
            ex is NotSupportedException
            or FileFormatException
            or InvalidOperationException
            or ArgumentException
            or OverflowException
            or COMException)
        {
            return ImageConversionResult.Failure(
                sourceFullPath,
                destFullPath,
                sourceFormat: null,
                targetFormat: targetFormat,
                title: "손상된 원본 이미지입니다.",
                message: "원본 파일이 손상되었거나 올바른 이미지 데이터가 아니어서 변환할 수 없습니다.");
        }
    }

    private static void DefaultEncodeToStream(
        BitmapSource sourceBitmap,
        ImageFormatKind targetFormat,
        Stream outputStream)
    {
        BitmapEncoder encoder = targetFormat switch
        {
            ImageFormatKind.Jpeg => new JpegBitmapEncoder { QualityLevel = DefaultJpegQualityLevel },
            ImageFormatKind.Png => new PngBitmapEncoder(),
            ImageFormatKind.Bmp => new BmpBitmapEncoder(),
            _ => throw new NotSupportedException(
                SupportedFileFormats.BuildUnsupportedConversionTargetMessage(
                    SupportedFileFormats.GetDisplayName(targetFormat))),
        };

        encoder.Frames.Add(BitmapFrame.Create(sourceBitmap));
        encoder.Save(outputStream);
    }

    private static void VerifyEncodedTempFile(
        string tempFilePath,
        ImageFormatKind expectedTargetFormat,
        int expectedWidth,
        int expectedHeight)
    {
        using var verifyStream = new FileStream(
            tempFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        var decoder = BitmapDecoder.Create(
            verifyStream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);

        if (!TryGetDecoderFormat(decoder, out var actualFormat) || actualFormat != expectedTargetFormat)
        {
            throw new FileFormatException("인코딩된 결과 파일의 실제 이미지 컨테이너 형식이 목표 형식과 일치하지 않습니다.");
        }

        if (decoder.Frames.Count == 0)
        {
            throw new FileFormatException("인코딩된 결과 파일에서 이미지 프레임을 확인할 수 없습니다.");
        }

        var frame = decoder.Frames[0];
        if (frame.PixelWidth != expectedWidth || frame.PixelHeight != expectedHeight)
        {
            throw new FileFormatException("인코딩된 결과 파일의 픽셀 해상도가 원본과 일치하지 않습니다.");
        }

        var rowStride = checked(((expectedWidth * Math.Max(1, frame.Format.BitsPerPixel)) + 7) / 8);
        var lastRowBuffer = new byte[rowStride];
        frame.CopyPixels(new Int32Rect(0, expectedHeight - 1, expectedWidth, 1), lastRowBuffer, rowStride, 0);
    }

    private static bool TryGetDecoderFormat(BitmapDecoder decoder, out ImageFormatKind format)
    {
        switch (decoder)
        {
            case JpegBitmapDecoder:
                format = ImageFormatKind.Jpeg;
                return true;
            case PngBitmapDecoder:
                format = ImageFormatKind.Png;
                return true;
            case BmpBitmapDecoder:
                format = ImageFormatKind.Bmp;
                return true;
            case GifBitmapDecoder:
                format = ImageFormatKind.Gif;
                return true;
            case TiffBitmapDecoder:
                format = ImageFormatKind.Tiff;
                return true;
            default:
                format = default;
                return false;
        }
    }

    private static string? SanitizePath(string? path) =>
        path?.Trim().Trim('"').Trim();

    private static bool TryGetFullPath(string path, out string fullPath)
    {
        try
        {
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            fullPath = string.Empty;
            return false;
        }
    }

    private static bool IsSameFileOrLinkTarget(string leftFullPath, string rightFullPath)
    {
        if (string.Equals(leftFullPath, rightFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var resolvedLeft = TryResolveLinkTargetFullPath(leftFullPath) ?? leftFullPath;
        var resolvedRight = TryResolveLinkTargetFullPath(rightFullPath) ?? rightFullPath;
        return string.Equals(resolvedLeft, resolvedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryResolveLinkTargetFullPath(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath))
            {
                return null;
            }

            var targetInfo = File.ResolveLinkTarget(fullPath, returnFinalTarget: true);
            return targetInfo?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static void CleanupFailedArtifacts(string tempFilePath, string? partialDestinationPath)
    {
        TryDeleteFile(tempFilePath);
        if (!string.IsNullOrWhiteSpace(partialDestinationPath))
        {
            TryDeleteFile(partialDestinationPath);
        }
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // 정리 단계에서 발생한 2차 예외는 삼켜 원래 실패 결과를 안전하게 반환합니다.
        }
    }
}
