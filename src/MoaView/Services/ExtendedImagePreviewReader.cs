using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;
using ImageMagick.Configuration;
using MoaView.Models;

namespace MoaView.Services;

internal static class ExtendedImagePreviewReader
{
    private static readonly Lazy<bool> Configured = new(Configure);

    internal static MagickFormat? GetDecoder(ImageFormatKind kind, string extension) => kind switch
    {
        ImageFormatKind.Photoshop => extension.Equals(".psb", StringComparison.OrdinalIgnoreCase) ? MagickFormat.Psb : MagickFormat.Psd,
        ImageFormatKind.Dds => MagickFormat.Dds,
        ImageFormatKind.Jpeg2000 => extension.Equals(".jp2", StringComparison.OrdinalIgnoreCase) ? MagickFormat.Jp2 : MagickFormat.J2k,
        ImageFormatKind.Targa => MagickFormat.Tga,
        ImageFormatKind.Pcx => MagickFormat.Pcx,
        ImageFormatKind.Netpbm => MagickFormat.Pnm,
        ImageFormatKind.JpegXl => MagickFormat.Jxl,
        ImageFormatKind.OpenExr => MagickFormat.Exr,
        ImageFormatKind.Qoi => MagickFormat.Qoi,
        ImageFormatKind.Heif => MagickFormat.Heic,
        ImageFormatKind.RadianceHdr => MagickFormat.Hdr,
        ImageFormatKind.CameraRaw => MagickFormat.Dng,
        _ => null,
    };

    internal static BitmapSource Read(byte[] data, MagickFormat format)
    {
        _ = Configured.Value;
        var settings = new MagickReadSettings { Format = format, FrameIndex = 0, FrameCount = 1 };
        if (format == MagickFormat.Heic)
            settings.SetDefine(MagickFormat.Heic, "primary-image", true);
        using var image = new MagickImage();
        image.Ping(data, settings);
        CheckDimensions(image.Width, image.Height);
        image.Read(data, settings);
        CheckDimensions(image.Width, image.Height);
        image.AutoOrient();
        image.ColorSpace = ColorSpace.sRGB;
        using var pixels = image.GetPixelsUnsafe();
        var buffer = pixels.ToByteArray("BGRA") ?? throw new InvalidDataException();
        var width = checked((int)image.Width);
        var height = checked((int)image.Height);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, buffer, checked(width * 4));
        bitmap.Freeze();
        return bitmap;
    }

    private static void CheckDimensions(uint width, uint height)
    {
        if (width == 0 || height == 0)
            throw new InvalidDataException();
        if ((ulong)width * height > 64_000_000)
            throw new PreviewLimitException("이미지 크기가 6,400만 픽셀 제한을 넘습니다.");
    }

    private static bool Configure()
    {
        // Accept only in-memory input from the selected file and never invoke external delegates.
        var configuration = ConfigurationFiles.Default;
        configuration.Policy.Data = """
            <policymap>
              <policy domain="delegate" rights="none" pattern="*" />
              <policy domain="filter" rights="none" pattern="*" />
              <policy domain="path" rights="none" pattern="@*" />
              <policy domain="coder" rights="none" pattern="*" />
              <policy domain="coder" rights="read" pattern="{PSD,PSB,DDS,JP2,J2K,TGA,PCX,PNM,PBM,PGM,PPM,PAM,JXL,EXR,QOI,HEIC,HEIF,HDR,DNG,JPEG,TIFF}" />
              <policy domain="resource" name="thread" value="2" />
              <policy domain="resource" name="time" value="10" />
              <policy domain="resource" name="width" value="32768" />
              <policy domain="resource" name="height" value="32768" />
              <policy domain="resource" name="area" value="64MP" />
              <policy domain="resource" name="memory" value="384MiB" />
              <policy domain="resource" name="map" value="0" />
              <policy domain="resource" name="disk" value="0" />
              <policy domain="resource" name="list-length" value="16" />
              <policy domain="system" name="max-memory-request" value="256MiB" />
              <policy domain="system" name="max-profile-size" value="8MiB" />
            </policymap>
            """;
        MagickNET.Initialize(configuration);
        return true;
    }
}
