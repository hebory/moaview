using System.Text;

namespace MoaView.Services;

internal static class TextPreviewReader
{
    public static string Read(byte[] data)
    {
        Encoding encoding = new UTF8Encoding(false, true);
        var skip = 0;
        if (data.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 }))
        {
            encoding = new UTF32Encoding(false, false, true); skip = 4;
        }
        else if (data.AsSpan().StartsWith(new byte[] { 0, 0, 0xFE, 0xFF }))
        {
            encoding = new UTF32Encoding(true, false, true); skip = 4;
        }
        else if (data.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            encoding = new UnicodeEncoding(false, false, true); skip = 2;
        }
        else if (data.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            encoding = new UnicodeEncoding(true, false, true); skip = 2;
        }
        else if (data.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) skip = 3;
        var text = encoding.GetString(data, skip, data.Length - skip);
        if (text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n' or '\f')))
            throw new NotSupportedException("일반 텍스트가 아닌 바이너리 내용은 표시하지 않습니다.");
        return text.Length > PreviewLimits.MaxTextCharacters
            ? text[..PreviewLimits.MaxTextCharacters] + "\n\n[미리보기 길이 제한으로 나머지 내용은 생략했습니다.]"
            : text;
    }
}
