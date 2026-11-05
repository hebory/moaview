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

    public static IReadOnlyCollection<string> ConvertibleImageExtensions =>
        [".jpg", ".jpeg", ".png", ".bmp"];

    public static IReadOnlyList<ImageFormatKind> ConvertibleImageFormats { get; } =
    [
        ImageFormatKind.Png,
        ImageFormatKind.Jpeg,
        ImageFormatKind.Bmp,
    ];

    public static IReadOnlyList<ConversionFormatOption> ConvertibleFormatOptions { get; } =
    [
        new(ImageFormatKind.Png, "PNG (.png)", ".png"),
        new(ImageFormatKind.Jpeg, "JPEG (.jpg)", ".jpg"),
        new(ImageFormatKind.Bmp, "BMP (.bmp)", ".bmp"),
    ];

    public const string SupportedFormatsShortText =
        "지원 이미지: JPEG, PNG, BMP, GIF, TIFF · ZIP · 문서 · Rive · PureRef";

    public const string SupportedFormatsDetailText =
        "이미지: JPEG, PNG, BMP, GIF, TIFF · ZIP 내부 이미지/텍스트 · DOCX, XLSX, PPTX, HWP 5.x, HWPX 내용 · RIV 애니메이션 · PUR 포함 PNG/JPEG · UTF-8/Unicode 텍스트 · 이미지 변환: JPEG, PNG, BMP";

    public const string SupportedConversionShortText = "변환 지원: JPEG, PNG, BMP";

    public const string SupportedConversionDetailText =
        "이미지 변환 지원 형식: JPEG (.jpg, .jpeg), PNG (.png), BMP (.bmp) 상호 변환";

    public const string OpenFileDialogFilter =
        "모든 지원 파일|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.zip;*.docx;*.xlsx;*.pptx;*.hwp;*.hwpx;*.riv;*.pur;*.txt;*.md;*.csv;*.tsv;*.json;*.xml;*.log|모든 파일 (*.*)|*.*|이미지|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|ZIP 압축파일|*.zip|문서 내용|*.docx;*.xlsx;*.pptx;*.hwp;*.hwpx|Rive 애니메이션|*.riv|PureRef 포함 이미지|*.pur|텍스트|*.txt;*.md;*.csv;*.tsv;*.json;*.xml;*.log;*.yaml;*.yml;*.ini;*.cs;*.js;*.ts;*.py;*.html;*.css";

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

    public static bool IsConvertibleFormat(ImageFormatKind format) =>
        format is ImageFormatKind.Jpeg or ImageFormatKind.Png or ImageFormatKind.Bmp;

    public static bool TryGetConvertibleFormatByExtension(string? extension, out ImageFormatKind format)
    {
        if (TryGetImageFormatByExtension(extension, out format) && IsConvertibleFormat(format))
        {
            return true;
        }

        format = default;
        return false;
    }

    public static bool IsMatchingExtensionForFormat(string? extension, ImageFormatKind format) =>
        TryGetImageFormatByExtension(extension, out var extFormat) && extFormat == format;

    public static string GetDefaultExtension(ImageFormatKind format) => format switch
    {
        ImageFormatKind.Jpeg => ".jpg",
        ImageFormatKind.Png => ".png",
        ImageFormatKind.Bmp => ".bmp",
        ImageFormatKind.Gif => ".gif",
        ImageFormatKind.Tiff => ".tiff",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    public static string BuildSaveDialogFilter(ImageFormatKind preferredFormat) => preferredFormat switch
    {
        ImageFormatKind.Jpeg =>
            "JPEG 이미지 (*.jpg;*.jpeg)|*.jpg;*.jpeg|PNG 이미지 (*.png)|*.png|BMP 이미지 (*.bmp)|*.bmp",
        ImageFormatKind.Bmp =>
            "BMP 이미지 (*.bmp)|*.bmp|PNG 이미지 (*.png)|*.png|JPEG 이미지 (*.jpg;*.jpeg)|*.jpg;*.jpeg",
        _ =>
            "PNG 이미지 (*.png)|*.png|JPEG 이미지 (*.jpg;*.jpeg)|*.jpg;*.jpeg|BMP 이미지 (*.bmp)|*.bmp",
    };

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

    public static string BuildUnsupportedConversionSourceMessage(ImageFormatKind sourceFormat) =>
        $"현재 이미지 형식({GetDisplayName(sourceFormat)})은 미리보기만 지원하며 이미지 변환은 지원하지 않습니다. {SupportedConversionDetailText}.";

    public static string BuildUnsupportedConversionSourceExtensionMessage(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return $"확장자가 없는 파일은 변환 형식을 확인할 수 없어 이미지 변환을 지원하지 않습니다. {SupportedConversionDetailText}.";
        }

        if (TryGetImageFormatByExtension(extension, out var previewOnlyFormat) && !IsConvertibleFormat(previewOnlyFormat))
        {
            return BuildUnsupportedConversionSourceMessage(previewOnlyFormat);
        }

        return $"선택한 원본 형식('{extension}')은 이미지 변환을 지원하지 않습니다. {SupportedConversionDetailText}.";
    }

    public static string BuildUnsupportedConversionTargetMessage(string targetDescription) =>
        $"지정한 출력 형식('{targetDescription}')으로의 변환은 지원하지 않습니다. {SupportedConversionDetailText}.";
}
