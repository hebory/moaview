namespace MoaView.Models;

/// <summary>
/// 이미지 변환 검증 및 실행 결과를 담는 불변 모델입니다.
/// </summary>
public sealed record ImageConversionResult
{
    public required ConversionStatusKind Status { get; init; }
    public string? SourceFilePath { get; init; }
    public string? OutputFilePath { get; init; }
    public ImageFormatKind? SourceFormat { get; init; }
    public ImageFormatKind? TargetFormat { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public long OutputFileSizeBytes { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public bool IsSuccess => Status == ConversionStatusKind.Succeeded;

    public static ImageConversionResult None() => new()
    {
        Status = ConversionStatusKind.None,
    };

    public static ImageConversionResult Ready(
        string sourceFilePath,
        ImageFormatKind? sourceFormat = null) => new()
    {
        Status = ConversionStatusKind.Succeeded,
        SourceFilePath = sourceFilePath,
        SourceFormat = sourceFormat,
    };

    public static ImageConversionResult Success(
        string sourceFilePath,
        string outputFilePath,
        ImageFormatKind sourceFormat,
        ImageFormatKind targetFormat,
        int pixelWidth,
        int pixelHeight,
        long outputFileSizeBytes,
        string title,
        string message) => new()
    {
        Status = ConversionStatusKind.Succeeded,
        SourceFilePath = sourceFilePath,
        OutputFilePath = outputFilePath,
        SourceFormat = sourceFormat,
        TargetFormat = targetFormat,
        PixelWidth = pixelWidth,
        PixelHeight = pixelHeight,
        OutputFileSizeBytes = outputFileSizeBytes,
        Title = title,
        Message = message,
    };

    public static ImageConversionResult Unsupported(
        string? sourceFilePath,
        string? outputFilePath,
        ImageFormatKind? sourceFormat,
        ImageFormatKind? targetFormat,
        string title,
        string message) => new()
    {
        Status = ConversionStatusKind.UnsupportedCombination,
        SourceFilePath = sourceFilePath,
        OutputFilePath = outputFilePath,
        SourceFormat = sourceFormat,
        TargetFormat = targetFormat,
        Title = title,
        Message = message,
    };

    public static ImageConversionResult OverwriteBlocked(
        string sourceFilePath,
        string outputFilePath,
        ImageFormatKind? sourceFormat,
        ImageFormatKind? targetFormat,
        string title,
        string message) => new()
    {
        Status = ConversionStatusKind.OriginalOverwriteBlocked,
        SourceFilePath = sourceFilePath,
        OutputFilePath = outputFilePath,
        SourceFormat = sourceFormat,
        TargetFormat = targetFormat,
        Title = title,
        Message = message,
    };

    public static ImageConversionResult Failure(
        string? sourceFilePath,
        string? outputFilePath,
        ImageFormatKind? sourceFormat,
        ImageFormatKind? targetFormat,
        string title,
        string message) => new()
    {
        Status = ConversionStatusKind.Failed,
        SourceFilePath = sourceFilePath,
        OutputFilePath = outputFilePath,
        SourceFormat = sourceFormat,
        TargetFormat = targetFormat,
        Title = title,
        Message = message,
    };
}
