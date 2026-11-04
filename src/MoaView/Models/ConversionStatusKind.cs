namespace MoaView.Models;

/// <summary>
/// 이미지 변환 요청의 처리 결과를 구분합니다.
/// </summary>
public enum ConversionStatusKind
{
    None,
    Succeeded,
    UnsupportedCombination,
    OriginalOverwriteBlocked,
    Failed,
}
