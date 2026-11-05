using System.Buffers.Binary;
using System.IO;
using System.Text;
using MoaView.Models;

namespace MoaView.Services;

/// <summary>
/// Reads self-contained PNG/JPEG payloads, not PureRef's scene graph. The header layout
/// is based on the public PureRef-format research; never infer that the first PNG is a board.
/// Linked files and URLs are deliberately not resolved.
/// </summary>
internal static class PurPreviewReader
{
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = CreateCrcTable();
    private const int MaxImages = 512;

    public static PurPreview Read(byte[] bytes, string path)
    {
        if (bytes.Length < 224) throw new InvalidDataException();
        var stringLength = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        if (stringLength is < 6 or > 32 || stringLength % 2 != 0) throw new InvalidDataException();
        var version = Encoding.BigEndianUnicode.GetString(bytes, 4, (int)stringLength);
        if (!Version.TryParse(version, out var parsed) || parsed.Major is not (1 or 2))
            throw new NotSupportedException("이 PureRef 저장 버전은 지원하지 않습니다. PNG로 내보낸 보드를 열어 주세요.");

        var images = new List<PurImageEntry>();
        var unreadable = 0;
        Exception? lastError = null;
        long workRemaining = 2L * PreviewLimits.MaxContainerBytes;
        // Search in file order. Valid images are skipped as a unit, so signatures inside
        // PNG chunks / JPEG metadata cannot become extra images or terminate an image early.
        for (var offset = 224; offset <= bytes.Length - 8; offset++)
        {
            var remaining = bytes.AsSpan(offset);
            (int length, int width, int height, string format) image;
            try
            {
                if (remaining.StartsWith(PngSignature))
                    image = ReadPng(remaining, ref workRemaining);
                else if (remaining[0] == 0xFF && remaining[1] == 0xD8 && remaining[2] == 0xFF)
                    image = ReadJpeg(remaining, ref workRemaining);
                else continue;
            }
            catch (Exception ex) when (workRemaining >= 0 && ex is (InvalidDataException or OverflowException or PreviewLimitException))
            {
                // Unrelated serialized data can contain image-like signatures. Recover
                // later images, but explicitly report partial results and bound retries.
                lastError ??= ex;
                if (++unreadable > 1024) throw new PreviewLimitException("PUR 이미지 탐색 한도를 넘었습니다.");
                continue;
            }

            if (images.Count == MaxImages) throw new PreviewLimitException("PUR 포함 이미지 수가 512개 제한을 넘습니다.");
            images.Add(new(images.Count, offset, image.length, image.format, image.width, image.height));
            offset += image.length - 1;
        }
        if (images.Count == 0 && lastError is not null) throw lastError;
        if (images.Count == 0)
            throw new NotSupportedException("PUR 파일에서 포함된 PNG/JPEG 이미지를 찾지 못했습니다. 외부 연결 이미지·메모만 있는 보드나 다른 저장 구조는 지원하지 않습니다. PureRef에서 보드를 PNG로 내보내 주세요.");
        return new PurPreview(path, images.AsReadOnly()) { Data = bytes, UnreadableCandidates = unreadable };
    }

    private static (int, int, int, string) ReadPng(ReadOnlySpan<byte> data, ref long workRemaining)
    {
        var position = 8;
        var width = 0;
        var height = 0;
        var hasPixels = false;
        while (position <= data.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
            CheckLength((long)position + length + 12);
            if (length > data.Length - position - 12) throw new InvalidDataException();
            var size = (int)length;
            var type = data.Slice(position + 4, 4);
            var chunk = data.Slice(position + 4, size + 4);
            SpendWork(ref workRemaining, chunk.Length);
            if (Crc(chunk) != BinaryPrimitives.ReadUInt32BigEndian(data[(position + size + 8)..]))
                throw new InvalidDataException("PNG CRC mismatch.");
            if (position == 8)
            {
                if (!type.SequenceEqual("IHDR"u8) || size != 13) throw new InvalidDataException();
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[(position + 8)..]));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[(position + 12)..]));
                CheckPixels(width, height);
            }
            else if (type.SequenceEqual("IHDR"u8)) throw new InvalidDataException();
            if (type.SequenceEqual("IDAT"u8)) hasPixels = true;
            position += size + 12;
            if (type.SequenceEqual("IEND"u8))
            {
                if (size != 0 || !hasPixels) throw new InvalidDataException();
                return (position, width, height, "PNG");
            }
        }
        throw new InvalidDataException();
    }

    private static (int, int, int, string) ReadJpeg(ReadOnlySpan<byte> data, ref long workRemaining)
    {
        var position = 2;
        var width = 0;
        var height = 0;
        var hasScan = false;
        var inScan = false;
        while (position < data.Length)
        {
            CheckLength(position);
            if (inScan)
            {
                var search = data.Slice(position, Math.Min(data.Length - position, PreviewLimits.MaxEntryBytes - position));
                var next = search.IndexOf((byte)0xFF);
                SpendWork(ref workRemaining, next < 0 ? search.Length : next + 1);
                if (next < 0 && data.Length - position > search.Length) CheckLength((long)PreviewLimits.MaxEntryBytes + 1);
                if (next < 0) throw new InvalidDataException();
                position += next;
            }
            if (data[position++] != 0xFF) throw new InvalidDataException();
            while (position < data.Length && data[position] == 0xFF) position++;
            if (position == data.Length) throw new InvalidDataException();
            var marker = data[position++];
            if (inScan && (marker == 0 || marker is >= 0xD0 and <= 0xD7)) continue;
            inScan = false;
            if (marker == 0xD9)
            {
                CheckLength(position);
                if (!hasScan || width == 0) throw new InvalidDataException();
                return (position, width, height, "JPEG");
            }
            if (marker is 0 or 0xD8 || marker is >= 0xD0 and <= 0xD7) throw new InvalidDataException();
            if (marker == 1) continue; // TEM, the only other standalone marker.
            if (position > data.Length - 2) throw new InvalidDataException();
            var size = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
            CheckLength((long)position + size);
            if (size < 2 || size > data.Length - position) throw new InvalidDataException();
            SpendWork(ref workRemaining, size);
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                if (size < 8) throw new InvalidDataException();
                height = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 3)..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 5)..]);
                CheckPixels(width, height);
            }
            position += size;
            if (marker == 0xDA) hasScan = inScan = true;
        }
        throw new InvalidDataException();
    }

    private static void CheckLength(long length)
    {
        if (length > PreviewLimits.MaxEntryBytes) throw new PreviewLimitException("PUR 포함 이미지가 32 MB 제한을 넘습니다.");
    }

    private static void SpendWork(ref long remaining, int bytes)
    {
        remaining -= bytes;
        if (remaining < 0) throw new PreviewLimitException("PUR 이미지 탐색 한도를 넘었습니다.");
    }

    private static void CheckPixels(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new InvalidDataException();
        if ((long)width * height > 64_000_000) throw new PreviewLimitException("PUR 포함 이미지가 6,400만 픽셀 제한을 넘습니다.");
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) != 0 ? 0xEDB88320u : 0);
            table[i] = value;
        }
        return table;
    }
}
