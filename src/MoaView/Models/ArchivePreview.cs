namespace MoaView.Models;

public sealed record ArchiveEntryInfo(int Index, string Name, long Size, long CompressedSize, bool IsDirectory, bool IsEncrypted)
{
    public string Description => IsDirectory ? "폴더" : IsEncrypted ? "암호화됨" : $"{Size:N0} B";
}

public sealed record ArchivePreview(string FilePath, IReadOnlyList<ArchiveEntryInfo> Entries)
{
    // The immutable snapshot keeps entry indexes stable and releases the original file handle immediately.
    internal byte[] Data { get; init; } = [];
}

public sealed record DocumentSection(string Title, string Text);
