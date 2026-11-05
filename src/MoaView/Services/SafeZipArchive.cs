using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using MoaView.Models;

namespace MoaView.Services;

/// <summary>Bounded, memory-only ZIP access. Checks flags and CRC rather than trusting extension or entry metadata.</summary>
internal sealed class SafeZipArchive : IDisposable
{
    private readonly MemoryStream _stream;
    private readonly ZipArchive _zip;
    private readonly List<(ushort Flags, uint Crc)> _metadata = new();
    public IReadOnlyList<ArchiveEntryInfo> Entries { get; }

    public SafeZipArchive(byte[] data)
    {
        if (data.Length > PreviewLimits.MaxContainerBytes)
            throw new PreviewLimitException("ZIP 파일이 128 MB 제한을 넘습니다.");
        ReadDirectory(data);
        _stream = new MemoryStream(data, writable: false);
        try
        {
            _zip = new ZipArchive(_stream, ZipArchiveMode.Read, leaveOpen: true);
            if (_zip.Entries.Count != _metadata.Count)
                throw new InvalidDataException("ZIP 항목 수가 일치하지 않습니다.");
            Entries = _zip.Entries.Select((e, i) => new ArchiveEntryInfo(i, e.FullName, e.Length,
                e.CompressedLength, e.FullName.EndsWith('/') || e.FullName.EndsWith('\\'),
                (_metadata[i].Flags & 0x41) != 0)).ToArray();
        }
        catch
        {
            _zip?.Dispose();
            _stream.Dispose();
            throw;
        }
    }

    public bool Contains(string name) => Entries.Any(e => e.Name == name);

    public byte[] Read(string name, int limit)
    {
        var matches = Entries.Where(e => e.Name == name).ToArray();
        if (matches.Length != 1) throw new InvalidDataException($"문서 구성 항목이 없거나 중복되었습니다: {name}");
        return Read(matches[0].Index, limit);
    }

    public byte[] Read(int index, int limit)
    {
        if ((uint)index >= Entries.Count) throw new InvalidDataException("ZIP 항목을 찾을 수 없습니다.");
        var info = Entries[index];
        if (info.IsEncrypted) throw new EncryptedPreviewException("암호화된 ZIP 항목은 미리볼 수 없습니다. 암호를 해제한 파일을 선택해 주세요.");
        if (info.Size > limit) throw new PreviewLimitException($"항목이 미리보기 크기 제한({limit / 1024 / 1024} MB)을 넘습니다.");
        using var stream = _zip.Entries[index].Open();
        var bytes = PreviewLimits.Read(stream, limit);
        if (bytes.LongLength != info.Size || ComputeCrc32(bytes) != _metadata[index].Crc)
            throw new InvalidDataException("ZIP 항목의 크기 또는 CRC가 일치하지 않습니다.");
        return bytes;
    }

    private void ReadDirectory(byte[] data)
    {
        var end = -1;
        for (var i = data.Length - 22; i >= Math.Max(0, data.Length - 65557); i--)
        {
            if (U32(data, i) == 0x06054b50 && i + 22 + U16(data, i + 20) == data.Length)
            {
                end = i;
                break;
            }
        }
        if (end < 0) throw new InvalidDataException("올바른 ZIP 중앙 디렉터리가 없습니다.");
        if (U16(data, end + 4) != 0 || U16(data, end + 6) != 0 || U16(data, end + 8) != U16(data, end + 10))
            throw new NotSupportedException("분할 ZIP은 미리보기를 지원하지 않습니다.");
        var count = U16(data, end + 10);
        var size = U32(data, end + 12);
        var start = U32(data, end + 16);
        if (count == ushort.MaxValue || size == uint.MaxValue || start == uint.MaxValue)
            throw new NotSupportedException("ZIP64 형식은 현재 미리보기를 지원하지 않습니다.");
        if (count > PreviewLimits.MaxEntries) throw new PreviewLimitException("ZIP 항목이 4,000개 제한을 넘습니다.");
        if ((long)start + size != end) throw new InvalidDataException("ZIP 디렉터리 범위가 올바르지 않습니다.");
        var offset = (int)start;
        for (var i = 0; i < count; i++)
        {
            if (offset > end - 46 || U32(data, offset) != 0x02014b50)
                throw new InvalidDataException("ZIP 항목 정보가 손상되었습니다.");
            var nameLength = U16(data, offset + 28);
            if (nameLength > 4096) throw new PreviewLimitException("ZIP 내부 파일 이름이 너무 깁니다.");
            if (U16(data, offset + 34) != 0) throw new NotSupportedException("분할 ZIP은 지원하지 않습니다.");
            _metadata.Add((U16(data, offset + 8), U32(data, offset + 16)));
            offset += 46 + nameLength + U16(data, offset + 30) + U16(data, offset + 32);
            if (offset > end) throw new InvalidDataException("ZIP 항목 범위가 올바르지 않습니다.");
        }
        if (offset != end) throw new InvalidDataException("ZIP 디렉터리 길이가 일치하지 않습니다.");
    }

    private static ushort U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(i =>
    {
        var value = (uint)i;
        for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
        return value;
    }).ToArray();

    private static uint ComputeCrc32(byte[] bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return ~crc;
    }

    public void Dispose()
    {
        _zip.Dispose();
        _stream.Dispose();
    }
}
