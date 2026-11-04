using System.Windows.Media.Imaging;

namespace MoaView.Models;

/// <summary>
/// 파일 미리보기 시도 결과를 담는 불변 모델입니다.
/// </summary>
public sealed record PreviewResult
{
    public required PreviewStateKind State { get; init; }
    public string? FilePath { get; init; }
    public string? FileName { get; init; }
    public string? Extension { get; init; }
    public ImageFormatKind? ImageFormat { get; init; }
    public BitmapSource? Bitmap { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public long FileSizeBytes { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public static PreviewResult Empty() => new()
    {
        State = PreviewStateKind.Empty,
        Title = "미리 볼 이미지 파일을 선택하세요",
        Message = "상단의 '파일 선택...' 버튼을 누르거나 실행 인수로 파일 경로를 전달하면 미리보기를 표시합니다.",
    };

    public static PreviewResult Image(
        string filePath,
        string fileName,
        string extension,
        ImageFormatKind format,
        BitmapSource bitmap,
        long fileSizeBytes) => new()
    {
        State = PreviewStateKind.ImageLoaded,
        FilePath = filePath,
        FileName = fileName,
        Extension = extension,
        ImageFormat = format,
        Bitmap = bitmap,
        PixelWidth = bitmap.PixelWidth,
        PixelHeight = bitmap.PixelHeight,
        FileSizeBytes = fileSizeBytes,
        Title = fileName,
        Message = string.Empty,
    };

    public static PreviewResult Unsupported(
        string filePath,
        string fileName,
        string extension,
        string title,
        string message) => new()
    {
        State = PreviewStateKind.UnsupportedFormat,
        FilePath = filePath,
        FileName = fileName,
        Extension = extension,
        Title = title,
        Message = message,
    };

    public static PreviewResult Failure(
        string? filePath,
        string? fileName,
        string? extension,
        string title,
        string message) => new()
    {
        State = PreviewStateKind.Error,
        FilePath = filePath,
        FileName = fileName,
        Extension = extension,
        Title = title,
        Message = message,
    };
}
