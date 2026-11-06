using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MoaView.Models;
using MoaView.ViewModels;
using MoaView.Controls;
using MoaView.Services;
using System.ComponentModel;

namespace MoaView;

public partial class MainWindow : Window
{
    private const double ViewportTotalPadding = 32.0;

    private bool _isPanning;
    private Point _panStartPoint;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;
    private RivePreviewControl? _rivePreview;
    private byte[]? _riveData;
    private SpecializedPreviewControl? _specializedPreview;
    private PreviewResult? _specializedResult;

    public MainWindow()
        : this(new MainWindowViewModel())
    {
    }

    public MainWindow(MainWindowViewModel viewModel, bool isExplorerPreview = false)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        IsExplorerPreview = isExplorerPreview;
        DataContext = viewModel;
        InitializeComponent();

        ExplorerSettingsGroup.Visibility = isExplorerPreview ? Visibility.Collapsed : Visibility.Visible;
        ExplorerPreviewHint.Visibility = isExplorerPreview ? Visibility.Visible : Visibility.Collapsed;
        SettingsPopup.CustomPopupPlacementCallback = PlaceFlyout;
        ImageOptionsPopup.CustomPopupPlacementCallback = PlaceFlyout;

        ViewModel.ViewResetRequested += OnViewModelViewResetRequested;
        ViewModel.PropertyChanged += OnPreviewContentChanged;
        Loaded += (_, _) => { SyncViewportSize(); UpdateRivePreview(); UpdateSpecializedPreview(); };
        Closed += (_, _) =>
        {
            SettingsPopup.IsOpen = false;
            ImageOptionsPopup.IsOpen = false;
            ViewModel.ViewResetRequested -= OnViewModelViewResetRequested;
            ViewModel.PropertyChanged -= OnPreviewContentChanged;
            _rivePreview?.Dispose();
            _specializedPreview?.Dispose();
        };
    }

    public MainWindowViewModel ViewModel { get; }

    public bool IsExplorerPreview { get; }
    public string RivePlaybackStatus => _rivePreview?.PlaybackStatus ?? "none";
    public Task<bool> WaitForRivePreviewAsync() => _rivePreview?.Ready ?? Task.FromResult(false);
    public Task<bool> WaitForSpecializedPreviewAsync() => _specializedPreview?.Ready ?? Task.FromResult(false);

    private void OnPreviewContentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ViewModel.IsImageVisible) ImageOptionsPopup.IsOpen = false;
        UpdateRivePreview();
        UpdateSpecializedPreview();
    }

    private static CustomPopupPlacement[] PlaceFlyout(Size popup, Size anchor, Point _) =>
    [
        new(new Point(anchor.Width - popup.Width, anchor.Height + 6), PopupPrimaryAxis.Horizontal),
        new(new Point(anchor.Width - popup.Width, -popup.Height - 6), PopupPrimaryAxis.Horizontal),
    ];

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        ImageOptionsPopup.IsOpen = false;
        ShowFlyout(SettingsPopup, SettingsCloseButton);
    }

    private void OnImageOptionsClick(object sender, RoutedEventArgs e)
    {
        SettingsPopup.IsOpen = false;
        ShowFlyout(ImageOptionsPopup, ImageOptionsCloseButton);
    }

    private void ShowFlyout(Popup popup, UIElement firstControl)
    {
        popup.IsOpen = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (popup.IsOpen) firstControl.Focus();
        }));
    }

    private static void CloseFlyout(Popup popup)
    {
        popup.IsOpen = false;
        if (popup.PlacementTarget is { IsVisible: true } anchor) anchor.Focus();
    }

    private void OnCloseSettingsClick(object sender, RoutedEventArgs e) => CloseFlyout(SettingsPopup);
    private void OnCloseImageOptionsClick(object sender, RoutedEventArgs e) => CloseFlyout(ImageOptionsPopup);

    private void OnSettingsPreviewKeyDown(object sender, KeyEventArgs e) =>
        HandleFlyoutEscape(SettingsPopup, MarkdownThemePicker.IsDropDownOpen, e);

    private void OnImageOptionsPreviewKeyDown(object sender, KeyEventArgs e) =>
        HandleFlyoutEscape(ImageOptionsPopup, ConvertFormatComboBox.IsDropDownOpen, e);

    private static void HandleFlyoutEscape(Popup popup, bool dropDownOpen, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || dropDownOpen) return;
        CloseFlyout(popup);
        e.Handled = true;
    }

    private void UpdateRivePreview()
    {
        var data = ViewModel.IsRiveVisible ? ViewModel.CurrentResult.RiveData : null;
        if (ReferenceEquals(data, _riveData)) return;
        _riveData = data;
        if (data is null)
        {
            _rivePreview?.Dispose();
            _rivePreview = null;
            RiveViewport.Children.Clear();
            return;
        }
        if (_rivePreview is null)
        {
            _rivePreview = new RivePreviewControl();
            _rivePreview.CloseRequested += (_, _) => { if (IsExplorerPreview) Close(); };
            RiveViewport.Children.Add(_rivePreview);
        }
        _ = _rivePreview.LoadAsync(data);
    }

    private void UpdateSpecializedPreview()
    {
        var result = ViewModel.IsSpecializedVisible ? ViewModel.CurrentResult : null;
        if (ReferenceEquals(_specializedResult, result)) return;
        _specializedResult = result;
        _specializedPreview?.Dispose();
        SpecializedViewport.Children.Clear();
        _specializedPreview = result?.SpecializedKind switch
        {
            SpecializedPreviewKind.Pdf when result.SpecializedData is { } pdf => new PdfPreviewControl(pdf),
            SpecializedPreviewKind.Media when result.FilePath is { } media => new LocalMediaPreviewControl(media),
            SpecializedPreviewKind.Font when result.FilePath is { } font => new FontPreviewControl(font),
            SpecializedPreviewKind.BrowserImage when result.SpecializedData is { } image &&
                FilePreviewService.BrowserImageContentType(result.Extension ?? "") is { } mime => new BrowserImagePreviewControl(image, mime),
            _ => null,
        };
        if (_specializedPreview is BrowserImagePreviewControl browser)
            browser.CloseRequested += (_, _) => { if (IsExplorerPreview) Close(); };
        if (_specializedPreview is not null) SpecializedViewport.Children.Add(_specializedPreview);
    }

    public event EventHandler? ExplorerIntegrationToggleRequested;

    public bool IsExplorerIntegrationRequested => ExplorerIntegrationToggle.IsChecked == true;

    public void SetExplorerIntegrationState(bool enabled, string message)
    {
        ExplorerIntegrationToggle.IsChecked = enabled;
        ExplorerIntegrationMessage.Text = message;
    }

    private void OnExplorerIntegrationToggleClick(object sender, RoutedEventArgs e) =>
        ExplorerIntegrationToggleRequested?.Invoke(this, EventArgs.Empty);

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

        var conversionBannerValid = ViewModel.HasConversionNotice
            ? ConversionNoticeBanner.IsVisible &&
              ConversionNoticeBadgeText.IsVisible &&
              ConversionNoticeMessageText.IsVisible &&
              (ViewModel.CanOpenConvertedFile == OpenConvertedFileButton.IsVisible)
            : !ConversionNoticeBanner.IsVisible;
        if (!conversionBannerValid)
        {
            return false;
        }

        return ViewModel.State switch
        {
            PreviewStateKind.Empty =>
                EmptyStatePanel.IsVisible &&
                EmptyStateTitle.IsVisible &&
                EmptyOpenFileButton.IsVisible &&
                !ViewerToolbar.IsVisible &&
                !ImageViewport.IsVisible &&
                !NoticePanel.IsVisible &&
                PreviewImage.Source is null,
            PreviewStateKind.ImageLoaded =>
                VerifyLoadedImageRenderedState() && (!ViewModel.IsPurVisible ||
                    PurPanel.IsVisible && PurImageList.IsVisible && Equals(PurImageList.SelectedItem, ViewModel.SelectedPurImage)),
            PreviewStateKind.ArchiveLoaded => ArchivePanel.IsVisible && ArchivePlaceholder.IsVisible && PreviewImage.Source is null,
            PreviewStateKind.TextLoaded => TextViewport.IsVisible &&
                (ViewModel.IsMarkdownRendered ? MarkdownPreview.IsVisible && MarkdownPreview.Document is not null && MarkdownToolbar.IsVisible :
                    PreviewText.IsVisible && PreviewText.Text == ViewModel.TextContent) && !ImageViewport.IsVisible && PreviewImage.Source is null,
            PreviewStateKind.RiveLoaded => RiveViewport.IsVisible && RivePlaybackStatus is "ready" or "error" &&
                !ImageViewport.IsVisible && PreviewImage.Source is null,
            PreviewStateKind.SpecializedLoaded => SpecializedViewport.IsVisible && _specializedPreview?.Ready.IsCompleted == true &&
                !ImageViewport.IsVisible && PreviewImage.Source is null,
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
        if (IsExplorerPreview && modifiers == ModifierKeys.None && key is Key.Space or Key.Escape)
        {
            Close();
            return true;
        }

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
                Key.S => ExecuteIfPossible(ViewModel.ConvertImageCommand, this),
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
        if (SettingsPopup.IsOpen || ImageOptionsPopup.IsOpen) return;
        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (MarkdownPreview.IsKeyboardFocusWithin && actualKey is not Key.Escape &&
            (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift ||
                Keyboard.Modifiers == ModifierKeys.Control && actualKey is Key.A or Key.C or Key.Home or Key.End))
            return;
        if (MarkdownSourceToggle.IsKeyboardFocusWithin && actualKey == Key.Space) return;
        if (e.OriginalSource is System.Windows.Controls.TextBox && actualKey is not Key.Escape && Keyboard.Modifiers != ModifierKeys.Control)
            return;
        if (ViewModel.IsArchiveVisible && ArchiveEntryList.IsKeyboardFocusWithin && actualKey is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            return;
        if (ViewModel.IsPurVisible && PurImageList.IsKeyboardFocusWithin && actualKey is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            return;
        if (IsExplorerPreview && e.IsRepeat && actualKey is Key.Space or Key.Escape)
        {
            e.Handled = true;
            return;
        }

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
        PreviewText.ScrollToHome();
        MarkdownPreview.Document?.Blocks.FirstBlock?.BringIntoView();
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
