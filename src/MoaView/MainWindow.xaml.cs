using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MoaView.Models;
using MoaView.ViewModels;

namespace MoaView;

public partial class MainWindow : Window
{
    private const double ViewportTotalPadding = 32.0;

    private bool _isPanning;
    private Point _panStartPoint;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;

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

        ViewModel.ViewResetRequested += OnViewModelViewResetRequested;
        Loaded += (_, _) => SyncViewportSize();
        Closed += (_, _) => ViewModel.ViewResetRequested -= OnViewModelViewResetRequested;
    }

    public MainWindowViewModel ViewModel { get; }

    public bool VerifyRenderedState()
    {
        UpdateLayout();
        SyncViewportSize();
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
                VerifyLoadedImageRenderedState(),
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

    public bool HandleKeyInput(Key key, ModifierKeys modifiers = ModifierKeys.None)
    {
        SyncViewportSize();

        if (modifiers == ModifierKeys.Control)
        {
            return key switch
            {
                Key.O => ExecuteIfPossible(ViewModel.OpenFileCommand, this),
                Key.Add or Key.OemPlus => ExecuteIfPossible(ViewModel.ZoomInCommand),
                Key.Subtract or Key.OemMinus => ExecuteIfPossible(ViewModel.ZoomOutCommand),
                Key.D0 or Key.NumPad0 => ExecuteIfPossible(ViewModel.ActualSizeCommand),
                Key.R => ExecuteIfPossible(ViewModel.RotateClockwiseCommand),
                _ => false,
            };
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            return key switch
            {
                Key.R => ExecuteIfPossible(ViewModel.RotateCounterClockwiseCommand),
                _ => false,
            };
        }

        if (modifiers != ModifierKeys.None)
        {
            return false;
        }

        return key switch
        {
            Key.Left or Key.PageUp or Key.Back => ExecuteIfPossible(ViewModel.PreviousImageCommand),
            Key.Right or Key.PageDown => ExecuteIfPossible(ViewModel.NextImageCommand),
            Key.Add or Key.OemPlus => ExecuteIfPossible(ViewModel.ZoomInCommand),
            Key.Subtract or Key.OemMinus => ExecuteIfPossible(ViewModel.ZoomOutCommand),
            Key.D0 or Key.NumPad0 or Key.Divide => ExecuteIfPossible(ViewModel.ActualSizeCommand),
            Key.F or Key.Multiply => ExecuteIfPossible(ViewModel.ToggleFitToWindowCommand),
            Key.R => ExecuteIfPossible(ViewModel.RotateClockwiseCommand),
            Key.L => ExecuteIfPossible(ViewModel.RotateCounterClockwiseCommand),
            _ => false,
        };
    }

    public bool HandleZoomMouseWheel(int delta)
    {
        if (delta == 0 || !ViewModel.IsImageVisible)
        {
            return false;
        }

        SyncViewportSize();
        return delta > 0 ? ViewModel.ZoomIn() : ViewModel.ZoomOut();
    }

    private bool VerifyLoadedImageRenderedState()
    {
        if (!ImageViewport.IsVisible ||
            !ImageScrollViewer.IsVisible ||
            !PreviewImage.IsVisible ||
            PreviewImage.ActualWidth <= 0 ||
            PreviewImage.ActualHeight <= 0 ||
            PreviewImage.Source is not BitmapSource { PixelWidth: > 0, PixelHeight: > 0 } bitmap ||
            bitmap.PixelWidth != ViewModel.DisplayPixelWidth ||
            bitmap.PixelHeight != ViewModel.DisplayPixelHeight ||
            EmptyStatePanel.IsVisible ||
            NoticePanel.IsVisible)
        {
            return false;
        }

        if (ViewModel.IsFitToWindow)
        {
            return ImageScrollViewer.ActualWidth <= 0 ||
                   ImageScrollViewer.ActualHeight <= 0 ||
                   (PreviewImage.ActualWidth <= ImageScrollViewer.ActualWidth + 1.0 &&
                    PreviewImage.ActualHeight <= ImageScrollViewer.ActualHeight + 1.0);
        }

        return Math.Abs(PreviewImage.ActualWidth - ViewModel.ImageDisplayWidth) < 1.0 &&
               Math.Abs(PreviewImage.ActualHeight - ViewModel.ImageDisplayHeight) < 1.0;
    }

    private static bool ExecuteIfPossible(ICommand command, object? parameter = null)
    {
        if (!command.CanExecute(parameter))
        {
            return false;
        }

        command.Execute(parameter);
        return true;
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HandleKeyInput(actualKey, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    private void OnContentHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncViewportSize();
    }

    private void OnImageScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncViewportSize();
    }

    private void OnViewModelViewResetRequested(object? sender, EventArgs e)
    {
        CancelPanning();
        ImageScrollViewer.ScrollToHorizontalOffset(0);
        ImageScrollViewer.ScrollToVerticalOffset(0);
    }

    private void SyncViewportSize()
    {
        if (ImageScrollViewer.IsVisible &&
            ImageScrollViewer.ActualWidth > 0 &&
            ImageScrollViewer.ActualHeight > 0)
        {
            ViewModel.UpdateViewportSize(ImageScrollViewer.ActualWidth, ImageScrollViewer.ActualHeight);
            return;
        }

        if (ContentHostGrid.ActualWidth > ViewportTotalPadding &&
            ContentHostGrid.ActualHeight > ViewportTotalPadding)
        {
            ViewModel.UpdateViewportSize(
                ContentHostGrid.ActualWidth - ViewportTotalPadding,
                ContentHostGrid.ActualHeight - ViewportTotalPadding);
        }
    }

    private void OnImageViewportPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
            HandleZoomMouseWheel(e.Delta))
        {
            e.Handled = true;
        }
    }

    private void OnImageViewportMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.IsFitToWindow ||
            !ViewModel.IsImageVisible ||
            (ImageScrollViewer.ScrollableWidth <= 0 && ImageScrollViewer.ScrollableHeight <= 0))
        {
            return;
        }

        _isPanning = true;
        _panStartPoint = e.GetPosition(ImageScrollViewer);
        _panStartHorizontalOffset = ImageScrollViewer.HorizontalOffset;
        _panStartVerticalOffset = ImageScrollViewer.VerticalOffset;
        ImageScrollViewer.Cursor = Cursors.SizeAll;
        ImageScrollViewer.CaptureMouse();
        e.Handled = true;
    }

    private void OnImageViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            CancelPanning();
            return;
        }

        var current = e.GetPosition(ImageScrollViewer);
        var deltaX = current.X - _panStartPoint.X;
        var deltaY = current.Y - _panStartPoint.Y;
        ImageScrollViewer.ScrollToHorizontalOffset(_panStartHorizontalOffset - deltaX);
        ImageScrollViewer.ScrollToVerticalOffset(_panStartVerticalOffset - deltaY);
        e.Handled = true;
    }

    private void OnImageViewportMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            CancelPanning();
            e.Handled = true;
        }
    }

    private void OnImageViewportLostMouseCapture(object sender, MouseEventArgs e)
    {
        CancelPanning();
    }

    private void CancelPanning()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        ImageScrollViewer.ClearValue(CursorProperty);
        if (ImageScrollViewer.IsMouseCaptured)
        {
            ImageScrollViewer.ReleaseMouseCapture();
        }
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
}
