using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MoaView.Services;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace MoaView.Controls;

internal sealed class PdfPreviewControl : SpecializedPreviewControl
{
    private const uint MaxPageDimension = 1800;
    private const ulong MaxRenderedBytes = 16 * 1024 * 1024;
    private readonly InMemoryRandomAccessStream _source = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Image _image = new() { Stretch = Stretch.Uniform, Margin = new Thickness(12) };
    private readonly Button _previous = new() { Content = "이전", MinWidth = 68, IsEnabled = false };
    private readonly Button _next = new() { Content = "다음", MinWidth = 68, IsEnabled = false };
    private readonly TextBlock _pageLabel = new()
    {
        Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White,
    };
    private PdfDocument? _document;
    private uint _page;

    public PdfPreviewControl(byte[] bytes)
    {
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 10),
        };
        toolbar.Children.Add(_previous);
        toolbar.Children.Add(_pageLabel);
        toolbar.Children.Add(_next);
        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Bottom);
        layout.Children.Add(toolbar);
        layout.Children.Add(_image);
        Children.Add(layout);

        Loaded += Start;
        _previous.Click += async (_, _) => await ShowPageAsync(_page - 1);
        _next.Click += async (_, _) => await ShowPageAsync(_page + 1);

        async void Start(object sender, RoutedEventArgs e)
        {
            Loaded -= Start;
            var cancellation = _lifetime.Token;
            try
            {
                using (var writer = new DataWriter(_source))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync().AsTask(cancellation);
                    writer.DetachStream();
                }
                _source.Seek(0);
                _document = await PdfDocument.LoadFromStreamAsync(_source).AsTask(cancellation);
                if (_document.PageCount == 0) throw new InvalidDataException();
                await ShowPageAsync(0);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (FilePreviewService.IsPreviewError(ex))
            {
                ShowError("PDF를 열 수 없습니다. 파일이 손상되었거나 암호로 보호되어 있을 수 있습니다.");
            }
        }
    }

    private async Task ShowPageAsync(uint index)
    {
        if (_document is null || IsDisposed || index >= _document.PageCount) return;
        _previous.IsEnabled = _next.IsEnabled = false;
        var cancellation = _lifetime.Token;
        try
        {
            using var page = _document.GetPage(index);
            var size = page.Size;
            if (size.Width <= 0 || size.Height <= 0 || !double.IsFinite(size.Width) || !double.IsFinite(size.Height))
                throw new InvalidDataException();
            var scale = MaxPageDimension / Math.Max(size.Width, size.Height);
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, Math.Round(size.Width * scale)),
                DestinationHeight = (uint)Math.Max(1, Math.Round(size.Height * scale)),
            };
            using var rendered = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(rendered, options).AsTask(cancellation);
            if (rendered.Size is 0 or > MaxRenderedBytes) throw new InvalidDataException();
            using var reader = new DataReader(rendered.GetInputStreamAt(0));
            var count = (uint)rendered.Size;
            if (await reader.LoadAsync(count).AsTask(cancellation) != count) throw new EndOfStreamException();
            var data = new byte[count];
            reader.ReadBytes(data);
            using var stream = new MemoryStream(data, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            _image.Source = bitmap;
            _page = index;
            _pageLabel.Text = $"{index + 1:N0} / {_document.PageCount:N0}";
            ShowReady();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (FilePreviewService.IsPreviewError(ex))
        {
            _image.Source = null;
            _page = index;
            _pageLabel.Text = $"{index + 1:N0} / {_document.PageCount:N0}";
            ShowError("이 PDF 페이지를 표시할 수 없습니다.");
        }
        finally
        {
            if (!IsDisposed)
            {
                _previous.IsEnabled = _page > 0;
                _next.IsEnabled = _page + 1 < _document.PageCount;
            }
        }
    }

    protected override void ReleaseResources()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _source.Dispose();
        _image.Source = null;
    }
}
