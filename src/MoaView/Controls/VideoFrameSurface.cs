using System.Runtime.InteropServices;
using WinRT;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MoaView.Controls;

/// <summary>Provides a CPU-readable Direct3D surface for MediaPlayer frame-server output.</summary>
internal sealed class VideoFrameSurface : IDisposable
{
    private const uint BgraSupport = 0x20;
    private const uint BindRenderTarget = 0x20;
    private const uint SdkVersion = 7;
    private const int DxgiFormatB8G8R8A8Unorm = 87;
    private const int D3D11CreateDeviceHardware = 1;
    private const int D3D11CreateDeviceWarp = 5;
    private const int MaximumDimension = 4096;
    private static readonly Guid DxgiSurfaceIid = new("CAFCB56C-6AC3-4889-BF47-9E23BBD260EC");
    private IntPtr _texture;
    private int _disposed;
    private readonly Windows.Storage.Streams.Buffer _buffer;
    private readonly byte[] _pixels;

    private VideoFrameSurface(IDirect3DSurface surface, IntPtr texture, int width, int height)
    {
        Surface = surface;
        _texture = texture;
        Width = width;
        Height = height;
        RowPitch = checked(width * 4);
        _pixels = new byte[checked(RowPitch * height)];
        _buffer = new Windows.Storage.Streams.Buffer((uint)_pixels.Length);
    }

    internal IDirect3DSurface Surface { get; }
    internal int Width { get; }
    internal int Height { get; }
    internal int RowPitch { get; }

    internal static VideoFrameSurface Create(int width, int height)
    {
        if (width is <= 0 or > MaximumDimension || height is <= 0 or > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(width), $"Video dimensions must be between 1 and {MaximumDimension} pixels.");
        _ = checked(width * height * 4);

        var featureLevels = new[] { 0xB100, 0xB000, 0xA100, 0xA000 };
        var result = D3D11CreateDevice(IntPtr.Zero, D3D11CreateDeviceHardware, IntPtr.Zero, BgraSupport,
            featureLevels, (uint)featureLevels.Length, SdkVersion, out var device, out _, out var context);
        if (result < 0)
        {
            Release(context);
            Release(device);
            result = D3D11CreateDevice(IntPtr.Zero, D3D11CreateDeviceWarp, IntPtr.Zero, BgraSupport,
                featureLevels, (uint)featureLevels.Length, SdkVersion, out device, out _, out context);
        }
        if (result < 0)
        {
            Release(context);
            Release(device);
            Marshal.ThrowExceptionForHR(result);
        }

        IntPtr texture = IntPtr.Zero;
        IDirect3DSurface? surface = null;
        try
        {
            var description = new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = DxgiFormatB8G8R8A8Unorm,
                SampleDescription = new SampleDescription { Count = 1, Quality = 0 },
                Usage = 0,
                BindFlags = BindRenderTarget,
                CpuAccessFlags = 0,
                MiscFlags = 0,
            };
            var vtable = Marshal.ReadIntPtr(device);
            var createTexture2D = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(
                Marshal.ReadIntPtr(vtable, 5 * IntPtr.Size));
            Marshal.ThrowExceptionForHR(createTexture2D(device, in description, IntPtr.Zero, out texture));
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(texture, in DxgiSurfaceIid, out var dxgiSurface));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(dxgiSurface, out var inspectable));
                try { surface = MarshalInterface<IDirect3DSurface>.FromAbi(inspectable); }
                finally { Marshal.Release(inspectable); }
            }
            finally { Marshal.Release(dxgiSurface); }

            var resultSurface = new VideoFrameSurface(surface, texture, width, height);
            texture = IntPtr.Zero;
            return resultSurface;
        }
        catch
        {
            surface?.Dispose();
            Release(texture);
            throw;
        }
        finally
        {
            Release(context);
            Release(device);
        }
    }

    internal async Task<byte[]> ReadPixelsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(Surface,
            BitmapAlphaMode.Ignore).AsTask(cancellationToken);
        // Callers serialize reads and consume the pixels before starting the next frame.
        bitmap.CopyToBuffer(_buffer);
        using var reader = DataReader.FromBuffer(_buffer);
        reader.ReadBytes(_pixels);
        return _pixels;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var texture = Interlocked.Exchange(ref _texture, IntPtr.Zero);
        try { Surface.Dispose(); }
        finally { Release(texture); }
    }

    private static void Release(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero) Marshal.Release(pointer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SampleDescription { public uint Count; public uint Quality; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Texture2DDescription
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public int Format;
        public SampleDescription SampleDescription;
        public uint Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTexture2DDelegate(IntPtr device, in Texture2DDescription description,
        IntPtr initialData, out IntPtr texture);

    [DllImport("d3d11.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
        [In] int[] featureLevels, uint featureLevelCount, uint sdkVersion, out IntPtr device,
        out int featureLevel, out IntPtr immediateContext);

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11SurfaceFromDXGISurface", ExactSpelling = true, PreserveSig = true)]
    private static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgiSurface, out IntPtr inspectable);
}
