using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MoaView.Models;

namespace MoaView.Services;

/// <summary>
/// 로컬 파일 경로를 검사하고 지원 이미지(JPEG, PNG, BMP, GIF, TIFF)를 안전하게 디코딩합니다.
/// 미지원 형식이나 손상된 파일은 예외를 외부로 전파하지 않고 안내 결과로 반환합니다.
/// </summary>
public sealed class ImagePreviewService : IPreviewService
{
    public PreviewResult Load(string? filePath)
    {
        var sanitizedPath = filePath?.Trim().Trim('"').Trim();
        if (string.IsNullOrWhiteSpace(sanitizedPath))
        {
            return PreviewResult.Failure(
                filePath,
                fileName: null,
                extension: null,
                title: "파일을 열 수 없습니다.",
                message: "유효한 파일 경로가 지정되지 않았습니다.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(sanitizedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return PreviewResult.Failure(
                filePath,
                fileName: null,
                extension: null,
                title: "파일 경로가 올바르지 않습니다.",
                message: "지정한 경로 형식을 해석할 수 없습니다.");
        }

        if (Directory.Exists(fullPath))
        {
            var folderName = Path.GetFileName(
                fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return PreviewResult.Unsupported(
                fullPath,
                folderName,
                extension: string.Empty,
                title: "폴더는 미리보기를 지원하지 않습니다.",
                message: $"폴더가 아닌 이미지 파일을 선택해 주세요. {SupportedFileFormats.SupportedFormatsDetailText}.");
        }

        var fileName = Path.GetFileName(fullPath);
        var extension = Path.GetExtension(fullPath);

        if (!File.Exists(fullPath))
        {
            return PreviewResult.Failure(
                fullPath,
                fileName,
                extension,
                title: "파일을 찾을 수 없습니다.",
                message: $"지정한 파일이 존재하지 않습니다: {fullPath}");
        }

        if (!SupportedFileFormats.TryGetImageFormatByExtension(extension, out _))
        {
            return PreviewResult.Unsupported(
                fullPath,
                fileName,
                extension,
                title: "지원하지 않는 파일 형식입니다.",
                message: SupportedFileFormats.BuildUnsupportedMessage(extension));
        }

        try
        {
            using var fileStream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var fileLength = fileStream.Length;
            if (fileLength == 0)
            {
                return PreviewResult.Failure(
                    fullPath,
                    fileName,
                    extension,
                    title: "이미지를 표시할 수 없습니다.",
                    message: "파일이 비어 있어 이미지 데이터를 읽을 수 없습니다.");
            }

            using var memoryStream = new MemoryStream((int)Math.Min(fileLength, 64 * 1024 * 1024));
            fileStream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            return Decode(memoryStream, fullPath, fileName, extension, fileLength);
        }
        catch (UnauthorizedAccessException)
        {
            return PreviewResult.Failure(
                fullPath,
                fileName,
                extension,
                title: "파일에 접근할 수 없습니다.",
                message: "파일 읽기 권한이 없어 미리보기를 열 수 없습니다.");
        }
        catch (IOException)
        {
            return PreviewResult.Failure(
                fullPath,
                fileName,
                extension,
                title: "파일을 읽을 수 없습니다.",
                message: "파일 입출력 오류가 발생해 미리보기를 열 수 없습니다.");
        }
        catch (Exception ex) when (
            ex is NotSupportedException
            or FileFormatException
            or InvalidOperationException
            or ArgumentException
            or OverflowException
            or COMException
            or PreviewLimitException)
        {
            return PreviewResult.Failure(
                fullPath,
                fileName,
                extension,
                title: "이미지를 표시할 수 없습니다.",
                message: "파일이 손상되었거나 올바른 이미지 데이터가 아니어서 열 수 없습니다.");
        }
    }

    public PreviewResult LoadBytes(byte[] bytes, string displayName)
    {
        var extension = Path.GetExtension(displayName);
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return Decode(stream, displayName, displayName, extension, bytes.LongLength);
        }
        catch (Exception ex) when (ex is IOException or FileFormatException or NotSupportedException or InvalidOperationException
            or ArgumentException or OverflowException or COMException or PreviewLimitException)
        {
            return PreviewResult.Failure(displayName, displayName, extension,
                "이미지를 표시할 수 없습니다.", ex is PreviewLimitException ? ex.Message : "파일이 손상되었거나 올바른 이미지 데이터가 아닙니다.");
        }
    }

    private static PreviewResult Decode(Stream stream, string fullPath, string fileName, string extension, long fileLength)
    {
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnDemand);

        if (!TryGetSupportedDecoderFormat(decoder, out var actualFormat))
        {
            return PreviewResult.Unsupported(
                fullPath,
                fileName,
                extension,
                title: "지원하지 않는 이미지 형식입니다.",
                message: $"파일 확장자는 '{extension}'이지만 실제 데이터 형식이 지원 범위 밖입니다. {SupportedFileFormats.SupportedFormatsDetailText}.");
        }

        if (decoder.Frames.Count == 0)
        {
            return PreviewResult.Failure(
                fullPath,
                fileName,
                extension,
                title: "이미지를 표시할 수 없습니다.",
                message: "이미지 프레임을 찾을 수 없습니다.");
        }

        var firstFrame = decoder.Frames[0];
        if (firstFrame.PixelWidth <= 0 || firstFrame.PixelHeight <= 0)
        {
            return PreviewResult.Failure(
                fullPath,
                fileName,
                extension,
                title: "이미지를 표시할 수 없습니다.",
                message: "유효하지 않은 이미지 크기입니다.");
        }

        if ((long)firstFrame.PixelWidth * firstFrame.PixelHeight > 64_000_000)
            throw new PreviewLimitException("이미지 크기가 6,400만 픽셀 제한을 넘습니다.");

        // 렌더링 시점 크래시를 막기 위해 픽셀 버퍼를 즉시 디코딩·복사하고 Freeze합니다.
        BitmapSource decodedSource = firstFrame.Format == PixelFormats.Bgra32
            ? firstFrame
            : new FormatConvertedBitmap(firstFrame, PixelFormats.Bgra32, null, 0);

        var detachedBitmap = new WriteableBitmap(decodedSource);
        detachedBitmap.Freeze();

        return PreviewResult.Image(
            fullPath,
            fileName,
            extension,
            actualFormat,
            detachedBitmap,
            fileLength);
    }

    private static bool TryGetSupportedDecoderFormat(BitmapDecoder decoder, out ImageFormatKind format)
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
}
