using MoaView.Models;

namespace MoaView.Services;

/// <summary>
/// 모아뷰가 현재 지원하는 파일 형식 정의와 표시 문구를 일관되게 관리합니다.
/// </summary>
public static class SupportedFileFormats
{
    private static readonly IReadOnlyDictionary<string, ImageFormatKind> ExtensionMap =
        new Dictionary<string, ImageFormatKind>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = ImageFormatKind.Jpeg,
            [".jpeg"] = ImageFormatKind.Jpeg,
            [".png"] = ImageFormatKind.Png,
            [".bmp"] = ImageFormatKind.Bmp,
            [".gif"] = ImageFormatKind.Gif,
            [".tif"] = ImageFormatKind.Tiff,
            [".tiff"] = ImageFormatKind.Tiff,
        };

    public static IReadOnlyCollection<string> SupportedImageExtensions =>
        [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"];

    public const string SupportedFormatsShortText = "지원 이미지: JPEG, PNG, BMP, GIF, TIFF";

    public const string SupportedFormatsDetailText =
        "지원 이미지 형식: JPEG (.jpg, .jpeg), PNG (.png), BMP (.bmp), GIF (.gif), TIFF (.tif, .tiff)";

    public const string OpenFileDialogFilter =
        "모든 파일 (*.*)|*.*|지원 이미지 (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff";

    public static bool TryGetImageFormatByExtension(string? extension, out ImageFormatKind format)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            format = default;
            return false;
        }

        var normalized = extension.StartsWith('.') ? extension : $".{extension}";
        return ExtensionMap.TryGetValue(normalized, out format);
    }

    public static string GetDisplayName(ImageFormatKind format) => format switch
    {
        ImageFormatKind.Jpeg => "JPEG",
        ImageFormatKind.Png => "PNG",
        ImageFormatKind.Bmp => "BMP",
        ImageFormatKind.Gif => "GIF",
        ImageFormatKind.Tiff => "TIFF",
        _ => format.ToString().ToUpperInvariant(),
    };

    public static string BuildUnsupportedMessage(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return $"확장자가 없는 파일은 지원 형식을 확인할 수 없어 미리보기를 표시하지 않습니다. {SupportedFormatsDetailText}.";
        }

        return $"선택한 형식('{extension}')은 현재 미리보기를 지원하지 않습니다. {SupportedFormatsDetailText}.";
    }
}
