namespace MoaView.Models;

/// <summary>
/// 파일 미리보기 화면의 현재 상태를 나타냅니다.
/// </summary>
public enum PreviewStateKind
{
    Empty,
    ImageLoaded,
    UnsupportedFormat,
    Error,
    ArchiveLoaded,
    TextLoaded,
    RiveLoaded,
}
