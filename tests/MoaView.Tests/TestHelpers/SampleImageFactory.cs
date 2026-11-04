using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MoaView.Models;

namespace MoaView.Tests.TestHelpers;

internal sealed class TempTestDirectory : IDisposable
{
    public TempTestDirectory()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "MoaViewTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public string GetPath(string fileName) => Path.Combine(DirectoryPath, fileName);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
        catch
        {
            // 테스트 임시 폴더 정리 중 발생하는 일시적 오류는 무시합니다.
        }
    }
}

internal static class SampleImageFactory
{
    public static string WriteValidImage(
        string filePath,
        ImageFormatKind format,
        int width,
        int height,
        Color? color = null)
    {
        var fill = color ?? Color.FromRgb(36, 132, 218);
        var stride = width * 4;
        var pixels = new byte[height * stride];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * stride) + (x * 4);
                pixels[offset + 0] = fill.B;
                pixels[offset + 1] = fill.G;
                pixels[offset + 2] = fill.R;
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride);

        BitmapEncoder encoder = format switch
        {
            ImageFormatKind.Jpeg => new JpegBitmapEncoder { QualityLevel = 95 },
            ImageFormatKind.Png => new PngBitmapEncoder(),
            ImageFormatKind.Bmp => new BmpBitmapEncoder(),
            ImageFormatKind.Gif => new GifBitmapEncoder(),
            ImageFormatKind.Tiff => new TiffBitmapEncoder(),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
        return filePath;
    }

    /// <summary>
    /// Windows WIC가 디코딩할 수 있는 1x1 32비트 ICO 바이너리를 생성합니다.
    /// (모아뷰 지원 범위 밖의 이미지 컨테이너 검증용)
    /// </summary>
    public static string WriteValidIcoFile(string filePath)
    {
        byte[] pngPayload;
        var onePixel = new byte[] { 80, 120, 200, 255 };
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, onePixel, 4);
        var pngEncoder = new PngBitmapEncoder();
        pngEncoder.Frames.Add(BitmapFrame.Create(source));
        using (var ms = new MemoryStream())
        {
            pngEncoder.Save(ms);
            pngPayload = ms.ToArray();
        }

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fs);

        // ICONDIR (6 bytes)
        writer.Write((ushort)0); // Reserved
        writer.Write((ushort)1); // Type: 1 = ICO
        writer.Write((ushort)1); // Count: 1 image

        // ICONDIRENTRY (16 bytes)
        writer.Write((byte)1);   // Width
        writer.Write((byte)1);   // Height
        writer.Write((byte)0);   // ColorCount
        writer.Write((byte)0);   // Reserved
        writer.Write((ushort)1); // Planes
        writer.Write((ushort)32); // BitCount
        writer.Write((uint)pngPayload.Length); // BytesInRes
        writer.Write((uint)22);  // ImageOffset (6 + 16)

        writer.Write(pngPayload);
        return filePath;
    }
}
