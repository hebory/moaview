using System.IO;

namespace MoaView.Services;

/// <summary>
/// 현재 파일과 같은 폴더에 있는 지원 이미지(JPEG, PNG, BMP, GIF, TIFF)를 자연 정렬 순서로 탐색합니다.
/// </summary>
public sealed class FolderImageNavigationService : IFolderNavigationService
{
    public FolderNavigationContext ResolveContext(string? currentFilePath)
    {
        if (!TryNormalizeTargetFilePath(currentFilePath, out var fullPath, out var directoryPath))
        {
            return FolderNavigationContext.Empty;
        }

        List<string> siblingImages;
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                return FolderNavigationContext.Empty;
            }

            siblingImages = [];
            foreach (var entryPath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly))
            {
                var extension = Path.GetExtension(entryPath);
                if (SupportedFileFormats.TryGetImageFormatByExtension(extension, out _))
                {
                    siblingImages.Add(Path.GetFullPath(entryPath));
                }
            }
        }
        catch (Exception ex) when (
            ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return FolderNavigationContext.Empty;
        }

        if (siblingImages.Count == 0)
        {
            return FolderNavigationContext.Empty;
        }

        siblingImages.Sort(NaturalFilePathComparer.Instance);

        var currentIndex = -1;
        for (var i = 0; i < siblingImages.Count; i++)
        {
            if (string.Equals(siblingImages[i], fullPath, StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = i;
                break;
            }
        }

        return new FolderNavigationContext(siblingImages, currentIndex);
    }

    public string? GetPreviousImagePath(string? currentFilePath)
    {
        var context = ResolveContext(currentFilePath);
        if (!context.CanMovePrevious)
        {
            return null;
        }

        if (context.HasCurrentInList)
        {
            var prevIndex = (context.CurrentIndex - 1 + context.TotalCount) % context.TotalCount;
            return context.SiblingImagePaths[prevIndex];
        }

        if (!TryNormalizeTargetFilePath(currentFilePath, out var fullPath, out _))
        {
            return null;
        }

        var insertionIndex = FindInsertionIndex(context.SiblingImagePaths, fullPath);
        return insertionIndex > 0
            ? context.SiblingImagePaths[insertionIndex - 1]
            : context.SiblingImagePaths[context.TotalCount - 1];
    }

    public string? GetNextImagePath(string? currentFilePath)
    {
        var context = ResolveContext(currentFilePath);
        if (!context.CanMoveNext)
        {
            return null;
        }

        if (context.HasCurrentInList)
        {
            var nextIndex = (context.CurrentIndex + 1) % context.TotalCount;
            return context.SiblingImagePaths[nextIndex];
        }

        if (!TryNormalizeTargetFilePath(currentFilePath, out var fullPath, out _))
        {
            return null;
        }

        var insertionIndex = FindInsertionIndex(context.SiblingImagePaths, fullPath);
        return insertionIndex < context.TotalCount
            ? context.SiblingImagePaths[insertionIndex]
            : context.SiblingImagePaths[0];
    }

    private static int FindInsertionIndex(IReadOnlyList<string> sortedPaths, string targetFullPath)
    {
        for (var i = 0; i < sortedPaths.Count; i++)
        {
            if (NaturalFilePathComparer.Instance.Compare(sortedPaths[i], targetFullPath) > 0)
            {
                return i;
            }
        }

        return sortedPaths.Count;
    }

    private static bool TryNormalizeTargetFilePath(
        string? filePath,
        out string fullPath,
        out string directoryPath)
    {
        fullPath = string.Empty;
        directoryPath = string.Empty;

        var sanitized = filePath?.Trim().Trim('"').Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(sanitized);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (Directory.Exists(fullPath))
        {
            return false;
        }

        var parentDir = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parentDir))
        {
            return false;
        }

        directoryPath = parentDir;
        return true;
    }

    /// <summary>
    /// 파일명 내 숫자 구간을 크기 순으로 비교하는 자연 정렬 비교자입니다.
    /// </summary>
    internal sealed class NaturalFilePathComparer : IComparer<string>
    {
        public static NaturalFilePathComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var nameX = Path.GetFileName(x.AsSpan());
            var nameY = Path.GetFileName(y.AsSpan());

            var tokenComparison = CompareNaturalSpans(nameX, nameY);
            if (tokenComparison != 0)
            {
                return tokenComparison;
            }

            var ignoreCaseName = nameX.CompareTo(nameY, StringComparison.OrdinalIgnoreCase);
            if (ignoreCaseName != 0)
            {
                return ignoreCaseName;
            }

            var ordinalName = nameX.CompareTo(nameY, StringComparison.Ordinal);
            if (ordinalName != 0)
            {
                return ordinalName;
            }

            var ignoreCasePath = StringComparer.OrdinalIgnoreCase.Compare(x, y);
            return ignoreCasePath != 0
                ? ignoreCasePath
                : StringComparer.Ordinal.Compare(x, y);
        }

        private static int CompareNaturalSpans(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
        {
            var i = 0;
            var j = 0;

            while (i < left.Length && j < right.Length)
            {
                var cx = left[i];
                var cy = right[j];

                var xDigit = char.IsAsciiDigit(cx);
                var yDigit = char.IsAsciiDigit(cy);

                if (xDigit && yDigit)
                {
                    var xEnd = i;
                    while (xEnd < left.Length && char.IsAsciiDigit(left[xEnd]))
                    {
                        xEnd++;
                    }

                    var yEnd = j;
                    while (yEnd < right.Length && char.IsAsciiDigit(right[yEnd]))
                    {
                        yEnd++;
                    }

                    var xTrim = left[i..xEnd].TrimStart('0');
                    var yTrim = right[j..yEnd].TrimStart('0');

                    if (xTrim.Length != yTrim.Length)
                    {
                        return xTrim.Length.CompareTo(yTrim.Length);
                    }

                    var digitCompare = xTrim.CompareTo(yTrim, StringComparison.Ordinal);
                    if (digitCompare != 0)
                    {
                        return digitCompare;
                    }

                    i = xEnd;
                    j = yEnd;
                    continue;
                }

                if (xDigit != yDigit)
                {
                    // 모든 ASCII 숫자는 ['0'..'9'] 연속 구간이므로 '0'과의 대소만으로 비숫자 문자와의 순서가 일관됩니다.
                    var representativeX = xDigit ? '0' : char.ToUpperInvariant(cx);
                    var representativeY = yDigit ? '0' : char.ToUpperInvariant(cy);
                    return representativeX.CompareTo(representativeY);
                }

                var upperCompare = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
                if (upperCompare != 0)
                {
                    return upperCompare;
                }

                i++;
                j++;
            }

            if (i < left.Length)
            {
                return 1;
            }

            if (j < right.Length)
            {
                return -1;
            }

            return 0;
        }
    }
}
