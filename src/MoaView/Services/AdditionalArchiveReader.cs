using System.IO;
using MoaView.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace MoaView.Services;

internal static class AdditionalArchiveReader
{
    public static string? GetFormat(string extension) => extension switch
    {
        ".rar" or ".cbr" => "RAR",
        ".7z" or ".cb7" => "7Z",
        ".tar" => "TAR",
        _ => null,
    };

    public static ArchivePreview Read(byte[] bytes, string path, string format)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = Open(stream, format);
        var entries = archive.Entries.Take(PreviewLimits.MaxEntries + 1).Select(Describe).ToArray();
        if (entries.Length > PreviewLimits.MaxEntries)
            throw new PreviewLimitException("압축 파일 항목이 4,000개 제한을 넘습니다.");
        return new ArchivePreview(path, entries) { Data = bytes, Format = format };
    }

    public static byte[] ReadEntry(ArchivePreview snapshot, ArchiveEntryInfo entry, int limit, bool prefixOnly)
    {
        using var stream = new MemoryStream(snapshot.Data, writable: false);
        using var archive = Open(stream, snapshot.Format);
        var source = archive.Entries.ElementAtOrDefault(entry.Index);
        if (source is null || Describe(source, entry.Index) != entry)
            throw new InvalidDataException();
        if (archive.IsSolid || source.IsSolid)
            throw new NotSupportedException("솔리드 압축 파일은 항목 목록을 표시합니다. 이 항목을 보려면 먼저 압축을 풀어 주세요.");
        if (!source.IsComplete || source.IsSplitAfter || !string.IsNullOrEmpty(source.LinkTarget))
            throw new NotSupportedException("분할되었거나 다른 항목을 가리키는 파일은 압축 내부에서 바로 열 수 없습니다.");
        if (!prefixOnly && entry.Size > limit)
            throw new PreviewLimitException($"압축 해제된 내용이 미리보기 크기 제한({limit / 1024 / 1024} MB)을 넘습니다.");
        using var content = source.OpenEntryStream();
        if (!prefixOnly) return PreviewLimits.Read(content, limit);
        var prefix = new byte[(int)Math.Min(entry.Size, limit)];
        var read = content.ReadAtLeast(prefix, prefix.Length, false);
        return read == prefix.Length ? prefix : prefix[..read];
    }

    private static IArchive Open(Stream stream, string format)
    {
        var archive = ArchiveFactory.OpenArchive(stream, ReaderOptions.ForExternalStream);
        var expected = format switch { "RAR" => ArchiveType.Rar, "7Z" => ArchiveType.SevenZip, _ => ArchiveType.Tar };
        if (archive.Type == expected) return archive;
        archive.Dispose();
        throw new InvalidDataException();
    }

    private static ArchiveEntryInfo Describe(IArchiveEntry entry, int index)
    {
        var name = entry.Key ?? string.Empty;
        if (name.Length > 8192 || entry.Size < 0)
            throw new InvalidDataException();
        return new(index, name, entry.Size, entry.CompressedSize, entry.IsDirectory, entry.IsEncrypted);
    }
}
