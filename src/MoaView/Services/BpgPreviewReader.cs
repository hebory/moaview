using System.IO;

namespace MoaView.Services;

internal static class BpgPreviewReader
{
    public const string ContentType = "image/bpg";

    public static string GetDecoderFile(ReadOnlySpan<byte> data)
    {
        if (data.Length < 9 || !data.StartsWith(new byte[] { 0x42, 0x50, 0x47, 0xFB }) || (data[4] & 15) > 6)
            throw new InvalidDataException();

        var offset = 6;
        var width = ReadInteger(data, ref offset);
        var height = ReadInteger(data, ref offset);
        if (width == 0 || height == 0 || offset == data.Length)
            throw new InvalidDataException();
        if ((long)width * height > 4_194_304)
            throw new PreviewLimitException("BPG 이미지가 419만 픽셀 미리보기 한도를 넘습니다.");

        var animated = (data[5] & 1) != 0;
        return animated && (data[4] & 15) == 0 ? "bpgdec8a.js" : "bpgdec.js";
    }

    private static int ReadInteger(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = 0;
        for (var count = 0; count < 5 && offset < data.Length; count++)
        {
            var next = data[offset++];
            if (value > int.MaxValue >> 7) throw new InvalidDataException();
            value = (value << 7) | (next & 127);
            if ((next & 128) == 0) return value;
        }
        throw new InvalidDataException();
    }
}
