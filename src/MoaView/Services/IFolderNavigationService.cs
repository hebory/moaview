namespace MoaView.Services;

/// <summary>
/// 현재 파일이 속한 폴더 내 지원 이미지 목록과 탐색 위치 정보를 나타냅니다.
/// </summary>
public readonly record struct FolderNavigationContext(
    IReadOnlyList<string> SiblingImagePaths,
    int CurrentIndex)
{
    public static FolderNavigationContext Empty { get; } = new(Array.Empty<string>(), -1);

    public int TotalCount => SiblingImagePaths.Count;

    public bool HasCurrentInList => CurrentIndex >= 0 && CurrentIndex < SiblingImagePaths.Count;

    public bool CanMovePrevious => HasCurrentInList ? TotalCount > 1 : TotalCount > 0;

    public bool CanMoveNext => HasCurrentInList ? TotalCount > 1 : TotalCount > 0;
}

/// <summary>
/// 같은 폴더 안의 지원 이미지 파일을 정렬된 순서로 이전·다음 탐색합니다.
/// </summary>
public interface IFolderNavigationService
{
    FolderNavigationContext ResolveContext(string? currentFilePath);

    string? GetPreviousImagePath(string? currentFilePath);

    string? GetNextImagePath(string? currentFilePath);
}
