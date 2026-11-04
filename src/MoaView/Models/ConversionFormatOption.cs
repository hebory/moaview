namespace MoaView.Models;

/// <summary>
/// UI에서 선택 가능한 이미지 변환 출력 형식 항목을 나타냅니다.
/// </summary>
public sealed record ConversionFormatOption(
    ImageFormatKind Format,
    string DisplayName,
    string DefaultExtension)
{
    public override string ToString() => DisplayName;
}
