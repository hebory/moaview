using MoaView.Models;

namespace MoaView.Services;

/// <summary>
/// JPEG, PNG, BMP 형식 간 안전한 이미지 변환과 사전 형식 검증을 담당합니다.
/// </summary>
public interface IImageConversionService
{
    ImageConversionResult ValidateCanConvertSource(
        string? sourceFilePath,
        ImageFormatKind? loadedSourceFormat = null);

    string BuildSuggestedOutputPath(
        string sourceFilePath,
        ImageFormatKind targetFormat);

    ImageConversionResult Convert(
        string? sourceFilePath,
        ImageFormatKind targetFormat,
        string? destinationFilePath);
}
