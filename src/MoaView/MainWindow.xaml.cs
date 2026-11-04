using System.Windows;
using System.Windows.Media.Imaging;
using MoaView.Models;
using MoaView.ViewModels;

namespace MoaView;

public partial class MainWindow : Window
{
    public MainWindow()
        : this(new MainWindowViewModel())
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public MainWindowViewModel ViewModel { get; }

    public bool VerifyRenderedState()
    {
        UpdateLayout();

        if (!IsLoaded ||
            !IsVisible ||
            PresentationSource.FromVisual(this) is null ||
            ActualWidth <= 0 ||
            ActualHeight <= 0)
        {
            return false;
        }

        return ViewModel.State switch
        {
            PreviewStateKind.Empty =>
                EmptyStatePanel.IsVisible &&
                EmptyStateTitle.IsVisible &&
                !ImageViewport.IsVisible &&
                !NoticePanel.IsVisible &&
                PreviewImage.Source is null,
            PreviewStateKind.ImageLoaded =>
                ImageViewport.IsVisible &&
                PreviewImage.IsVisible &&
                PreviewImage.ActualWidth > 0 &&
                PreviewImage.ActualHeight > 0 &&
                PreviewImage.Source is BitmapSource { PixelWidth: > 0, PixelHeight: > 0 } &&
                !EmptyStatePanel.IsVisible &&
                !NoticePanel.IsVisible,
            PreviewStateKind.UnsupportedFormat or PreviewStateKind.Error =>
                NoticePanel.IsVisible &&
                NoticeTitleText.IsVisible &&
                NoticeMessageText.IsVisible &&
                !ImageViewport.IsVisible &&
                !EmptyStatePanel.IsVisible &&
                PreviewImage.Source is null,
            _ => false,
        };
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (TryOpenDataObject(e.Data))
        {
            e.Handled = true;
        }
    }

    public bool TryOpenDataObject(IDataObject? data)
    {
        if (data is null || !data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        if (data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } droppedPaths &&
            !string.IsNullOrWhiteSpace(droppedPaths[0]))
        {
            ViewModel.OpenFile(droppedPaths[0]);
            return true;
        }

        return false;
    }
}
