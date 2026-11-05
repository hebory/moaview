using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using MoaView.Models;

namespace MoaView.Services;

internal static class HwpPreviewReader
{
    public static IReadOnlyList<DocumentSection> Read(byte[] data)
    {
        using var storage = new CompoundStorage(data);
        var header = storage.ReadStream("FileHeader", 4096);
        if (header.Length < 256 || !header.AsSpan(0, 17).SequenceEqual("HWP Document File"u8) || header[35] != 5)
            throw new NotSupportedException("HWP 5.x 문서만 지원합니다. 이전 버전은 HWPX로 저장한 뒤 열어 주세요.");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(36, 4));
        if ((flags & ((1u << 1) | (1u << 2) | (1u << 4) | (1u << 8) | (1u << 10))) != 0)
            throw new EncryptedPreviewException("암호·배포용·DRM 보호 HWP 문서는 미리보기를 지원하지 않습니다.");
        using var body = storage.OpenStorage("BodyText");
        var sections = body.StreamNames().Where(n => n.StartsWith("Section", StringComparison.Ordinal) && int.TryParse(n.AsSpan(7), out _))
            .OrderBy(n => int.Parse(n.AsSpan(7))).ToArray();
        if (sections.Length == 0) throw new InvalidDataException("HWP 본문 구역이 없습니다.");
        if (sections.Length > 256) throw new PreviewLimitException("HWP 구역이 256개 제한을 넘습니다.");
        var result = new List<DocumentSection>();
        var remainingBytes = PreviewLimits.MaxEntryBytes;
        var remainingCharacters = PreviewLimits.MaxTextCharacters;
        foreach (var name in sections)
        {
            var records = body.ReadStream(name, remainingBytes);
            if ((flags & 1) != 0)
            {
                using var input = new MemoryStream(records, writable: false);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                records = PreviewLimits.Read(deflate, remainingBytes);
            }
            remainingBytes -= records.Length;
            var text = ReadRecords(records, remainingCharacters);
            remainingCharacters -= Math.Min(remainingCharacters, text.Length);
            result.Add(new DocumentSection($"구역 {result.Count + 1} · 본문/표 텍스트", string.IsNullOrWhiteSpace(text) ? "표시할 텍스트가 없습니다." : text));
            if (remainingCharacters == 0) break;
        }
        return result;
    }

    private static string ReadRecords(byte[] records, int limit)
    {
        var text = new StringBuilder();
        var offset = 0;
        var count = 0;
        while (offset < records.Length)
        {
            if (++count > 500_000) throw new PreviewLimitException("HWP 레코드 수가 너무 많습니다.");
            if (records.Length - offset < 4) throw new InvalidDataException("HWP 레코드 헤더가 잘렸습니다.");
            var header = BinaryPrimitives.ReadUInt32LittleEndian(records.AsSpan(offset, 4));
            offset += 4;
            var tag = header & 0x3FF;
            var size = header >> 20;
            if (size == 0xFFF)
            {
                if (records.Length - offset < 4) throw new InvalidDataException("HWP 확장 크기가 잘렸습니다.");
                size = BinaryPrimitives.ReadUInt32LittleEndian(records.AsSpan(offset, 4));
                offset += 4;
            }
            if (size > records.Length - offset) throw new InvalidDataException("HWP 레코드 데이터가 잘렸습니다.");
            if (tag == 67)
            {
                if ((size & 1) != 0) throw new InvalidDataException("HWP 본문 문자 길이가 올바르지 않습니다.");
                var payload = records.AsSpan(offset, (int)size);
                for (var index = 0; index < payload.Length && text.Length < limit; index += 2)
                {
                    var character = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(index, 2));
                    if (character >= 32) text.Append((char)character);
                    else if (character is 10 or 13) text.Append('\n');
                    else if (character == 24) text.Append('-');
                    else if (character is 30 or 31) text.Append(' ');
                    else if (character is >= 1 and <= 23)
                    {
                        if (index + 16 > payload.Length) throw new InvalidDataException("HWP 제어 문자가 잘렸습니다.");
                        if (character == 9) text.Append('\t');
                        index += 14; // Inline/extended controls occupy eight UTF-16 code units.
                    }
                }
                if (text.Length < limit) text.Append('\n');
            }
            else if (tag == 77 && text.Length + 5 < limit) text.Append("\n[표]\n");
            offset += (int)size;
            if (text.Length >= limit) return text.ToString() + "\n[미리보기 길이 제한으로 나머지 내용은 생략했습니다.]";
        }
        return text.ToString();
    }
}

/// <summary>Uses Windows structured storage to handle FAT/mini-FAT safely; no temp extraction or Office automation.</summary>
internal sealed class CompoundStorage : IDisposable
{
    private readonly IStorage _storage;
    private readonly ILockBytes? _bytes;

    public CompoundStorage(byte[] data)
    {
        if (data.Length > PreviewLimits.MaxContainerBytes) throw new PreviewLimitException("HWP 파일이 너무 큽니다.");
        try
        {
            Marshal.ThrowExceptionForHR(CreateILockBytesOnHGlobal(0, true, out _bytes));
            _bytes.WriteAt(0, data, (uint)data.Length, out var written);
            if (written != data.Length) throw new InvalidDataException("HWP 데이터를 읽지 못했습니다.");
            Marshal.ThrowExceptionForHR(StgOpenStorageOnILockBytes(_bytes, null, 0x10, 0, 0, out _storage));
        }
        catch
        {
            if (_bytes is not null) Marshal.ReleaseComObject(_bytes);
            throw;
        }
    }

    private CompoundStorage(IStorage storage) => _storage = storage;

    public CompoundStorage OpenStorage(string name)
    {
        _storage.OpenStorage(name, null, 0x10, 0, 0, out var child);
        return new CompoundStorage(child);
    }

    public byte[] ReadStream(string name, int limit)
    {
        _storage.OpenStream(name, 0, 0x10, 0, out var stream);
        var count = Marshal.AllocHGlobal(4);
        try
        {
            stream.Stat(out var info, 1);
            if (info.cbSize < 0 || info.cbSize > limit) throw new PreviewLimitException("HWP 내부 항목이 크기 제한을 넘습니다.");
            var data = new byte[(int)info.cbSize];
            stream.Read(data, data.Length, count);
            if (Marshal.ReadInt32(count) != data.Length) throw new InvalidDataException("HWP 스트림이 잘렸습니다.");
            return data;
        }
        finally
        {
            Marshal.FreeHGlobal(count);
            Marshal.ReleaseComObject(stream);
        }
    }

    public IReadOnlyList<string> StreamNames()
    {
        _storage.EnumElements(0, 0, 0, out var enumerator);
        try
        {
            var names = new List<string>();
            while (enumerator.Next(1, out var item, out var fetched) == 0 && fetched == 1)
            {
                if (names.Count >= PreviewLimits.MaxEntries) throw new PreviewLimitException("HWP 내부 항목 수가 너무 많습니다.");
                if (item.type == 2) names.Add(item.pwcsName);
            }
            return names;
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    public void Dispose()
    {
        Marshal.ReleaseComObject(_storage);
        if (_bytes is not null) Marshal.ReleaseComObject(_bytes);
    }

    [DllImport("ole32.dll")]
    private static extern int CreateILockBytesOnHGlobal(nint memory, [MarshalAs(UnmanagedType.Bool)] bool deleteOnRelease, out ILockBytes bytes);
    [DllImport("ole32.dll")]
    private static extern int StgOpenStorageOnILockBytes(ILockBytes bytes, IStorage? priority, uint mode, nint exclude, uint reserved, out IStorage storage);

    [ComImport, Guid("0000000A-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ILockBytes
    {
        void ReadAt(ulong offset, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] data, uint count, out uint read);
        void WriteAt(ulong offset, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] data, uint count, out uint written);
    }

    [ComImport, Guid("0000000B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IStorage
    {
        void CreateStream([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint reserved1, uint reserved2, out IStream stream);
        void OpenStream([MarshalAs(UnmanagedType.LPWStr)] string name, nint reserved1, uint mode, uint reserved2, out IStream stream);
        void CreateStorage([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint reserved1, uint reserved2, out IStorage storage);
        void OpenStorage([MarshalAs(UnmanagedType.LPWStr)] string name, IStorage? priority, uint mode, nint exclude, uint reserved, out IStorage storage);
        void CopyTo(uint count, nint interfaces, nint exclude, IStorage destination);
        void MoveElementTo([MarshalAs(UnmanagedType.LPWStr)] string name, IStorage destination, [MarshalAs(UnmanagedType.LPWStr)] string newName, uint flags);
        void Commit(uint flags);
        void Revert();
        void EnumElements(uint reserved1, nint reserved2, uint reserved3, out IEnumStorage enumerator);
    }

    [ComImport, Guid("0000000D-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumStorage
    {
        [PreserveSig] int Next(uint count, out STATSTG element, out uint fetched);
    }
}
