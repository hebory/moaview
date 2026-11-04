using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MoaView.Models;
using MoaView.Services;

namespace MoaView.ViewModels;

/// <summary>
/// 메인 미리보기 창의 상태와 파일 선택·열기·이미지 보기 조작 흐름을 관리하는 뷰모델입니다.
/// </summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    public const double MinZoomFactor = 0.10;
    public const double MaxZoomFactor = 32.00;
    public const double DefaultZoomFactor = 1.00;

    public static readonly IReadOnlyList<double> ZoomLevels =
    [
        0.10,
        0.25,
        0.33,
        0.50,
        0.67,
        0.75,
        1.00,
        1.25,
        1.50,
        2.00,
        3.00,
        4.00,
        6.00,
        8.00,
        12.00,
        16.00,
        24.00,
        32.00,
    ];

    private readonly IPreviewService _previewService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IFolderNavigationService _folderNavigationService;

    private readonly RelayCommand _openFileCommand;
    private readonly RelayCommand _previousImageCommand;
    private readonly RelayCommand _nextImageCommand;
    private readonly RelayCommand _zoomInCommand;
    private readonly RelayCommand _zoomOutCommand;
    private readonly RelayCommand _actualSizeCommand;
    private readonly RelayCommand _toggleFitToWindowCommand;
    private readonly RelayCommand _rotateClockwiseCommand;
    private readonly RelayCommand _rotateCounterClockwiseCommand;

    private PreviewResult _currentResult = PreviewResult.Empty();
    private FolderNavigationContext _navigationContext = FolderNavigationContext.Empty;
    private BitmapSource? _transformedPreviewBitmap;
    private bool _isFitToWindow = true;
    private double _zoomFactor = DefaultZoomFactor;
    private int _rotationDegrees;
    private double _viewportWidth;
    private double _viewportHeight;

    public MainWindowViewModel()
        : this(new ImagePreviewService(), new Win32FileDialogService(), new FolderImageNavigationService())
    {
    }

    public MainWindowViewModel(IPreviewService previewService, IFileDialogService fileDialogService)
        : this(previewService, fileDialogService, new FolderImageNavigationService())
    {
    }

    public MainWindowViewModel(
        IPreviewService previewService,
        IFileDialogService fileDialogService,
        IFolderNavigationService folderNavigationService)
    {
        ArgumentNullException.ThrowIfNull(previewService);
        ArgumentNullException.ThrowIfNull(fileDialogService);
        ArgumentNullException.ThrowIfNull(folderNavigationService);

        _previewService = previewService;
        _fileDialogService = fileDialogService;
        _folderNavigationService = folderNavigationService;

        _openFileCommand = new RelayCommand(param => SelectAndOpenFile(param as Window));
        _previousImageCommand = new RelayCommand(_ => MovePreviousImage(), _ => CanNavigatePrevious);
        _nextImageCommand = new RelayCommand(_ => MoveNextImage(), _ => CanNavigateNext);
        _zoomInCommand = new RelayCommand(_ => ZoomIn(), _ => CanZoomIn);
        _zoomOutCommand = new RelayCommand(_ => ZoomOut(), _ => CanZoomOut);
        _actualSizeCommand = new RelayCommand(_ => ResetZoom(), _ => IsImageVisible);
        _toggleFitToWindowCommand = new RelayCommand(_ => ToggleFitToWindow(), _ => IsImageVisible);
        _rotateClockwiseCommand = new RelayCommand(_ => RotateClockwise(), _ => IsImageVisible);
        _rotateCounterClockwiseCommand = new RelayCommand(_ => RotateCounterClockwise(), _ => IsImageVisible);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? ViewResetRequested;

    public ICommand OpenFileCommand => _openFileCommand;

    public ICommand PreviousImageCommand => _previousImageCommand;

    public ICommand NextImageCommand => _nextImageCommand;

    public ICommand ZoomInCommand => _zoomInCommand;

    public ICommand ZoomOutCommand => _zoomOutCommand;

    public ICommand ActualSizeCommand => _actualSizeCommand;

    public ICommand ToggleFitToWindowCommand => _toggleFitToWindowCommand;

    public ICommand RotateClockwiseCommand => _rotateClockwiseCommand;

    public ICommand RotateCounterClockwiseCommand => _rotateCounterClockwiseCommand;

    public PreviewResult CurrentResult => _currentResult;

    public PreviewStateKind State => _currentResult.State;

    public bool IsEmptyState => State == PreviewStateKind.Empty;

    public bool IsImageVisible => State == PreviewStateKind.ImageLoaded && PreviewBitmap is not null;

    public bool IsNoticeVisible => State is PreviewStateKind.UnsupportedFormat or PreviewStateKind.Error;

    public Visibility EmptyStateVisibility => IsEmptyState ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ImageVisibility => IsImageVisible ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoticeVisibility => IsNoticeVisible ? Visibility.Visible : Visibility.Collapsed;

    public BitmapSource? PreviewBitmap => State == PreviewStateKind.ImageLoaded ? _transformedPreviewBitmap : null;

    public int DisplayPixelWidth => PreviewBitmap?.PixelWidth ?? 0;

    public int DisplayPixelHeight => PreviewBitmap?.PixelHeight ?? 0;

    public bool IsFitToWindow => _isFitToWindow;

    public double ZoomFactor => _zoomFactor;

    public double EffectiveZoomFactor => IsImageVisible ? GetEffectiveScale() : DefaultZoomFactor;

    public int EffectiveZoomPercent =>
        Math.Max(1, (int)Math.Round(EffectiveZoomFactor * 100.0, MidpointRounding.AwayFromZero));

    public int RotationDegrees => _rotationDegrees;

    public double ImageDisplayWidth =>
        !IsImageVisible || _isFitToWindow
            ? double.NaN
            : Math.Max(1.0, DisplayPixelWidth * _zoomFactor);

    public double ImageDisplayHeight =>
        !IsImageVisible || _isFitToWindow
            ? double.NaN
            : Math.Max(1.0, DisplayPixelHeight * _zoomFactor);

    public Stretch ImageStretch => _isFitToWindow ? Stretch.Uniform : Stretch.Fill;

    public ScrollBarVisibility HorizontalScrollBarVisibility =>
        !IsImageVisible || _isFitToWindow
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;

    public ScrollBarVisibility VerticalScrollBarVisibility =>
        !IsImageVisible || _isFitToWindow
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;

    public bool CanNavigatePrevious => _navigationContext.CanMovePrevious;

    public bool CanNavigateNext => _navigationContext.CanMoveNext;

    public bool CanZoomIn => IsImageVisible && GetEffectiveScale() < MaxZoomFactor - 0.005;

    public bool CanZoomOut => IsImageVisible && GetEffectiveScale() > MinZoomFactor + 0.005;

    public int CurrentFolderIndex =>
        _navigationContext.HasCurrentInList ? _navigationContext.CurrentIndex + 1 : 0;

    public int CurrentFolderTotalCount => _navigationContext.TotalCount;

    public string FolderPositionDisplay =>
        _navigationContext.TotalCount == 0
            ? "- / -"
            : _navigationContext.HasCurrentInList
                ? string.Create(CultureInfo.InvariantCulture, $"{_navigationContext.CurrentIndex + 1} / {_navigationContext.TotalCount}")
                : string.Create(CultureInfo.InvariantCulture, $"- / {_navigationContext.TotalCount}");

    public string ZoomDisplay
    {
        get
        {
            if (!IsImageVisible)
            {
                return "화면 맞춤";
            }

            if (_isFitToWindow)
            {
                return HasValidViewportSize
                    ? string.Create(CultureInfo.InvariantCulture, $"화면 맞춤 ({EffectiveZoomPercent}%)")
                    : "화면 맞춤";
            }

            var manualPercent = Math.Max(1, (int)Math.Round(_zoomFactor * 100.0, MidpointRounding.AwayFromZero));
            return string.Create(CultureInfo.InvariantCulture, $"{manualPercent}%");
        }
    }

    public string RotationDisplay =>
        string.Create(CultureInfo.InvariantCulture, $"{_rotationDegrees}°");

    public string WindowTitle => string.IsNullOrWhiteSpace(_currentResult.FileName)
        ? "모아뷰 (MoaView)"
        : $"{_currentResult.FileName} - 모아뷰 (MoaView)";

    public string StatusTitle => _currentResult.Title;

    public string StatusMessage => _currentResult.Message;

    public string NoticeBadgeText => State switch
    {
        PreviewStateKind.UnsupportedFormat => "미지원 형식",
        PreviewStateKind.Error => "열기 실패",
        _ => string.Empty,
    };

    public string SupportedFormatsSummary => SupportedFileFormats.SupportedFormatsShortText;

    public string SupportedFormatsDetail => SupportedFileFormats.SupportedFormatsDetailText;

    public string CurrentFilePathDisplay => string.IsNullOrWhiteSpace(_currentResult.FilePath)
        ? "선택된 파일 없음"
        : _currentResult.FilePath!;

    public string MetadataSummary
    {
        get
        {
            if (State == PreviewStateKind.ImageLoaded && _currentResult.ImageFormat is { } format)
            {
                var baseSummary =
                    $"{SupportedFileFormats.GetDisplayName(format)} · {_currentResult.PixelWidth} × {_currentResult.PixelHeight} px · {FormatFileSize(_currentResult.FileSizeBytes)}";

                var rotationPart = _rotationDegrees != 0
                    ? string.Create(CultureInfo.InvariantCulture, $" · 회전 {_rotationDegrees}°")
                    : string.Empty;

                var viewPart = _isFitToWindow
                    ? " · 화면 맞춤"
                    : string.Create(CultureInfo.InvariantCulture, $" · {(int)Math.Round(_zoomFactor * 100.0, MidpointRounding.AwayFromZero)}%");

                var folderPart = _navigationContext.HasCurrentInList
                    ? string.Create(CultureInfo.InvariantCulture, $" · {_navigationContext.CurrentIndex + 1}/{_navigationContext.TotalCount}")
                    : string.Empty;

                return $"{baseSummary}{rotationPart}{viewPart}{folderPart}";
            }

            return State switch
            {
                PreviewStateKind.UnsupportedFormat => "미지원 형식",
                PreviewStateKind.Error => "미리보기 불가",
                _ => "대기 중",
            };
        }
    }

    private bool HasValidViewportSize =>
        _viewportWidth > 1.0 &&
        _viewportHeight > 1.0 &&
        !double.IsNaN(_viewportWidth) &&
        !double.IsNaN(_viewportHeight) &&
        !double.IsInfinity(_viewportWidth) &&
        !double.IsInfinity(_viewportHeight);

    public bool SelectAndOpenFile(Window? owner = null)
    {
        var selectedPath = _fileDialogService.PickFile(owner);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return false;
        }

        OpenFile(selectedPath);
        return true;
    }

    public PreviewResult OpenFile(string? filePath)
    {
        var result = _previewService.Load(filePath);
        _currentResult = result;
        _isFitToWindow = true;
        _zoomFactor = DefaultZoomFactor;
        _rotationDegrees = 0;

        RebuildPreviewBitmap();
        _navigationContext = _folderNavigationService.ResolveContext(result.FilePath ?? filePath);

        NotifyAllStatePropertiesChanged();
        ViewResetRequested?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public bool MoveNextImage()
    {
        var nextPath = _folderNavigationService.GetNextImagePath(_currentResult.FilePath);
        if (string.IsNullOrWhiteSpace(nextPath))
        {
            RefreshNavigationContext();
            return false;
        }

        OpenFile(nextPath);
        return true;
    }

    public bool MovePreviousImage()
    {
        var previousPath = _folderNavigationService.GetPreviousImagePath(_currentResult.FilePath);
        if (string.IsNullOrWhiteSpace(previousPath))
        {
            RefreshNavigationContext();
            return false;
        }

        OpenFile(previousPath);
        return true;
    }

    public bool ZoomIn()
    {
        if (!IsImageVisible)
        {
            return false;
        }

        var baseline = GetEffectiveScale();
        var nextZoom = MaxZoomFactor;
        foreach (var level in ZoomLevels)
        {
            if (level > baseline + 0.005)
            {
                nextZoom = level;
                break;
            }
        }

        if (baseline >= MaxZoomFactor - 0.005)
        {
            return false;
        }

        _isFitToWindow = false;
        _zoomFactor = nextZoom;
        NotifyAllStatePropertiesChanged();
        return true;
    }

    public bool ZoomOut()
    {
        if (!IsImageVisible)
        {
            return false;
        }

        var baseline = GetEffectiveScale();
        var prevZoom = MinZoomFactor;
        for (var i = ZoomLevels.Count - 1; i >= 0; i--)
        {
            if (ZoomLevels[i] < baseline - 0.005)
            {
                prevZoom = ZoomLevels[i];
                break;
            }
        }

        if (baseline <= MinZoomFactor + 0.005)
        {
            return false;
        }

        _isFitToWindow = false;
        _zoomFactor = prevZoom;
        NotifyAllStatePropertiesChanged();
        return true;
    }

    public bool ResetZoom()
    {
        if (!IsImageVisible)
        {
            return false;
        }

        _isFitToWindow = false;
        _zoomFactor = DefaultZoomFactor;
        NotifyAllStatePropertiesChanged();
        return true;
    }

    public bool ToggleFitToWindow()
    {
        if (!IsImageVisible)
        {
            return false;
        }

        _isFitToWindow = !_isFitToWindow;
        NotifyAllStatePropertiesChanged();
        if (_isFitToWindow)
        {
            ViewResetRequested?.Invoke(this, EventArgs.Empty);
        }

        return _isFitToWindow;
    }

    public void SetFitToWindow(bool fitToWindow)
    {
        if (!IsImageVisible || _isFitToWindow == fitToWindow)
        {
            return;
        }

        _isFitToWindow = fitToWindow;
        NotifyAllStatePropertiesChanged();
        if (_isFitToWindow)
        {
            ViewResetRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool RotateClockwise()
    {
        if (!IsImageVisible)
        {
            return false;
        }

        _rotationDegrees = (_rotationDegrees + 90) % 360;
        RebuildPreviewBitmap();
        NotifyAllStatePropertiesChanged();
        return true;
    }

    public bool RotateCounterClockwise()
    {
        if (!IsImageVisible)
        {
            return false;
        }

        _rotationDegrees = (_rotationDegrees + 270) % 360;
        RebuildPreviewBitmap();
        NotifyAllStatePropertiesChanged();
        return true;
    }

    public void UpdateViewportSize(double viewportWidth, double viewportHeight)
    {
        var sanitizedWidth = double.IsFinite(viewportWidth) && viewportWidth > 0 ? viewportWidth : 0;
        var sanitizedHeight = double.IsFinite(viewportHeight) && viewportHeight > 0 ? viewportHeight : 0;

        if (Math.Abs(_viewportWidth - sanitizedWidth) < 0.5 &&
            Math.Abs(_viewportHeight - sanitizedHeight) < 0.5)
        {
            return;
        }

        _viewportWidth = sanitizedWidth;
        _viewportHeight = sanitizedHeight;

        if (IsImageVisible && _isFitToWindow)
        {
            NotifyAllStatePropertiesChanged();
        }
    }

    private double GetEffectiveScale()
    {
        if (!_isFitToWindow)
        {
            return _zoomFactor;
        }

        if (!HasValidViewportSize || DisplayPixelWidth <= 0 || DisplayPixelHeight <= 0)
        {
            return _zoomFactor;
        }

        var scaleX = _viewportWidth / DisplayPixelWidth;
        var scaleY = _viewportHeight / DisplayPixelHeight;
        return Math.Max(0.01, Math.Min(scaleX, scaleY));
    }

    private void RebuildPreviewBitmap()
    {
        if (_currentResult.State != PreviewStateKind.ImageLoaded || _currentResult.Bitmap is null)
        {
            _transformedPreviewBitmap = null;
            return;
        }

        if (_rotationDegrees == 0)
        {
            _transformedPreviewBitmap = _currentResult.Bitmap;
            return;
        }

        var transformed = new TransformedBitmap(
            _currentResult.Bitmap,
            new RotateTransform(_rotationDegrees));
        transformed.Freeze();
        _transformedPreviewBitmap = transformed;
    }

    private void RefreshNavigationContext()
    {
        _navigationContext = _folderNavigationService.ResolveContext(_currentResult.FilePath);
        NotifyAllStatePropertiesChanged();
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        }

        if (bytes < 1024 * 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:F1} KB");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):F2} MB");
    }

    private void NotifyAllStatePropertiesChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        _openFileCommand.RaiseCanExecuteChanged();
        _previousImageCommand.RaiseCanExecuteChanged();
        _nextImageCommand.RaiseCanExecuteChanged();
        _zoomInCommand.RaiseCanExecuteChanged();
        _zoomOutCommand.RaiseCanExecuteChanged();
        _actualSizeCommand.RaiseCanExecuteChanged();
        _toggleFitToWindowCommand.RaiseCanExecuteChanged();
        _rotateClockwiseCommand.RaiseCanExecuteChanged();
        _rotateCounterClockwiseCommand.RaiseCanExecuteChanged();
    }
}
