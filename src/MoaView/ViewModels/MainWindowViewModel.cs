using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MoaView.Models;
using MoaView.Services;

namespace MoaView.ViewModels;

/// <summary>
/// 메인 미리보기 창의 상태와 파일 선택·열기·이미지 보기 조작·형식 변환 흐름을 관리하는 뷰모델입니다.
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
    private readonly ISaveFileDialogService _saveFileDialogService;
    private readonly IFolderNavigationService _folderNavigationService;
    private readonly IImageConversionService _imageConversionService;
    private readonly FilePreviewService _contentService = new();
    private ArchiveEntryInfo? _selectedArchiveEntry;
    private DocumentSection? _selectedDocumentSection;
    private readonly IMarkdownThemeSettings _markdownThemeSettings;
    private MarkdownThemeOption _selectedMarkdownTheme;
    private bool _showMarkdownSource;
    private string _markdownSettingsMessage = string.Empty;

    private readonly RelayCommand _openFileCommand;
    private readonly RelayCommand _previousImageCommand;
    private readonly RelayCommand _nextImageCommand;
    private readonly RelayCommand _zoomInCommand;
    private readonly RelayCommand _zoomOutCommand;
    private readonly RelayCommand _actualSizeCommand;
    private readonly RelayCommand _toggleFitToWindowCommand;
    private readonly RelayCommand _rotateClockwiseCommand;
    private readonly RelayCommand _rotateCounterClockwiseCommand;
    private readonly RelayCommand _convertImageCommand;
    private readonly RelayCommand _openConvertedFileCommand;
    private readonly RelayCommand _dismissConversionNoticeCommand;

    private PreviewResult _currentResult = PreviewResult.Empty();
    private FolderNavigationContext _navigationContext = FolderNavigationContext.Empty;
    private ImageConversionResult _lastConversionResult = ImageConversionResult.None();
    private ImageFormatKind _selectedConversionFormat = ImageFormatKind.Png;
    private BitmapSource? _transformedPreviewBitmap;
    private bool _isFitToWindow = true;
    private double _zoomFactor = DefaultZoomFactor;
    private int _rotationDegrees;
    private double _viewportWidth;
    private double _viewportHeight;

    public MainWindowViewModel()
        : this(CreateDefaultDialogService())
    {
    }

    private MainWindowViewModel(Win32FileDialogService defaultDialogService)
        : this(
            new FilePreviewService(),
            defaultDialogService,
            defaultDialogService,
            new FolderImageNavigationService(),
            new ImageConversionService())
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
        : this(
            previewService,
            fileDialogService,
            fileDialogService as ISaveFileDialogService ?? new Win32FileDialogService(),
            folderNavigationService,
            new ImageConversionService())
    {
    }

    public MainWindowViewModel(
        IPreviewService previewService,
        IFileDialogService fileDialogService,
        ISaveFileDialogService saveFileDialogService,
        IFolderNavigationService? folderNavigationService = null,
        IImageConversionService? imageConversionService = null,
        IMarkdownThemeSettings? markdownThemeSettings = null)
    {
        ArgumentNullException.ThrowIfNull(previewService);
        ArgumentNullException.ThrowIfNull(fileDialogService);
        ArgumentNullException.ThrowIfNull(saveFileDialogService);

        _previewService = previewService;
        _fileDialogService = fileDialogService;
        _saveFileDialogService = saveFileDialogService;
        _folderNavigationService = folderNavigationService ?? new FolderImageNavigationService();
        _imageConversionService = imageConversionService ?? new ImageConversionService();
        _markdownThemeSettings = markdownThemeSettings ?? new MarkdownThemeSettings();
        _selectedMarkdownTheme = MarkdownThemeOption.For(_markdownThemeSettings.Load());

        _openFileCommand = new RelayCommand(param => SelectAndOpenFile(param as Window));
        _previousImageCommand = new RelayCommand(_ => MovePreviousImage(), _ => CanNavigatePrevious);
        _nextImageCommand = new RelayCommand(_ => MoveNextImage(), _ => CanNavigateNext);
        _zoomInCommand = new RelayCommand(_ => ZoomIn(), _ => CanZoomIn);
        _zoomOutCommand = new RelayCommand(_ => ZoomOut(), _ => CanZoomOut);
        _actualSizeCommand = new RelayCommand(_ => ResetZoom(), _ => IsImageVisible);
        _toggleFitToWindowCommand = new RelayCommand(_ => ToggleFitToWindow(), _ => IsImageVisible);
        _rotateClockwiseCommand = new RelayCommand(_ => RotateClockwise(), _ => IsImageVisible);
        _rotateCounterClockwiseCommand = new RelayCommand(_ => RotateCounterClockwise(), _ => IsImageVisible);
        _convertImageCommand = new RelayCommand(param => ConvertCurrentImage(param as Window), _ => CanExecuteConversion);
        _openConvertedFileCommand = new RelayCommand(_ => OpenConvertedFile(), _ => CanOpenConvertedFile);
        _dismissConversionNoticeCommand = new RelayCommand(_ => DismissConversionNotice(), _ => HasConversionNotice);
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

    public ICommand ConvertImageCommand => _convertImageCommand;

    public ICommand OpenConvertedFileCommand => _openConvertedFileCommand;

    public ICommand DismissConversionNoticeCommand => _dismissConversionNoticeCommand;

    public IReadOnlyList<ConversionFormatOption> AvailableConversionFormats =>
        SupportedFileFormats.ConvertibleFormatOptions;

    public ImageFormatKind SelectedConversionFormat
    {
        get => _selectedConversionFormat;
        set
        {
            if (!SupportedFileFormats.IsConvertibleFormat(value) || _selectedConversionFormat == value)
            {
                return;
            }

            _selectedConversionFormat = value;
            NotifyAllStatePropertiesChanged();
        }
    }

    public ConversionFormatOption SelectedConversionFormatOption
    {
        get
        {
            foreach (var option in AvailableConversionFormats)
            {
                if (option.Format == _selectedConversionFormat)
                {
                    return option;
                }
            }

            return AvailableConversionFormats[0];
        }
        set
        {
            if (value is not null)
            {
                SelectedConversionFormat = value.Format;
            }
        }
    }

    public bool CanExecuteConversion => !IsEmptyState && !IsArchiveVisible && !IsPurVisible && State is not (PreviewStateKind.TextLoaded or PreviewStateKind.RiveLoaded);

    public bool CanConvertLoadedFormat =>
        !IsArchiveVisible && !IsPurVisible &&
        State == PreviewStateKind.ImageLoaded &&
        _currentResult.ImageFormat is { } format &&
        SupportedFileFormats.IsConvertibleFormat(format);

    public ImageConversionResult LastConversionResult => _lastConversionResult;

    public ConversionStatusKind ConversionStatus => _lastConversionResult.Status;

    public bool HasConversionNotice => _lastConversionResult.Status != ConversionStatusKind.None;

    public Visibility ConversionNoticeVisibility =>
        HasConversionNotice ? Visibility.Visible : Visibility.Collapsed;

    public bool CanOpenConvertedFile =>
        _lastConversionResult.IsSuccess &&
        !string.IsNullOrWhiteSpace(_lastConversionResult.OutputFilePath) &&
        File.Exists(_lastConversionResult.OutputFilePath);

    public Visibility OpenConvertedFileVisibility =>
        CanOpenConvertedFile ? Visibility.Visible : Visibility.Collapsed;

    public string ConversionNoticeBadgeText => _lastConversionResult.Status switch
    {
        ConversionStatusKind.Succeeded => "변환 완료",
        ConversionStatusKind.UnsupportedCombination => "변환 미지원",
        ConversionStatusKind.OriginalOverwriteBlocked => "원본 보호",
        ConversionStatusKind.Failed => "변환 실패",
        _ => string.Empty,
    };

    public string ConversionNoticeTitle => _lastConversionResult.Title;

    public string ConversionNoticeMessage => _lastConversionResult.Message;

    public string SupportedConversionSummary => SupportedFileFormats.SupportedConversionShortText;

    public PreviewResult CurrentResult => _currentResult;

    public bool IsArchiveVisible => _currentResult.Archive is not null;
    public Visibility ArchiveVisibility => IsArchiveVisible ? Visibility.Visible : Visibility.Collapsed;
    public GridLength ArchiveColumnWidth => IsArchiveVisible || IsPurVisible ? new GridLength(240) : new GridLength(0);
    public IReadOnlyList<ArchiveEntryInfo> ArchiveEntries => _currentResult.Archive?.Entries ?? Array.Empty<ArchiveEntryInfo>();
    public string ArchiveSummary => $"ZIP · {ArchiveEntries.Count:N0}개 항목";
    public Visibility ArchivePlaceholderVisibility => State == PreviewStateKind.ArchiveLoaded ? Visibility.Visible : Visibility.Collapsed;
    public bool IsTextVisible => State == PreviewStateKind.TextLoaded;
    public Visibility TextVisibility => IsTextVisible ? Visibility.Visible : Visibility.Collapsed;
    public bool IsRiveVisible => State == PreviewStateKind.RiveLoaded;
    public Visibility RiveVisibility => IsRiveVisible ? Visibility.Visible : Visibility.Collapsed;
    public IReadOnlyList<DocumentSection> DocumentSections => _currentResult.Sections;
    public string TextContent => _selectedDocumentSection?.Text ?? string.Empty;
    public string ContentDescription => IsMarkdown ? "Markdown · 서식 미리보기 / 원문 보기" : _currentResult.ContentDescription;

    public bool IsMarkdown => IsTextVisible && string.Equals(_currentResult.Extension, ".md", StringComparison.OrdinalIgnoreCase);
    public Visibility MarkdownToolbarVisibility => IsMarkdown ? Visibility.Visible : Visibility.Collapsed;
    public bool IsMarkdownRendered => IsMarkdown && !ShowMarkdownSource;
    public Visibility MarkdownRenderedVisibility => IsMarkdownRendered ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TextSourceVisibility => IsTextVisible && !IsMarkdownRendered ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DocumentSectionVisibility => IsTextVisible && !IsMarkdown && DocumentSections.Count > 1
        ? Visibility.Visible : Visibility.Collapsed;
    public string MarkdownContent => IsMarkdown ? TextContent : string.Empty;
    public IReadOnlyList<MarkdownThemeOption> MarkdownThemes => MarkdownThemeOption.All;
    public Brush TextBackground => (IsMarkdown ? SelectedMarkdownTheme : MarkdownThemeOption.All[0]).Background;
    public Brush TextForeground => (IsMarkdown ? SelectedMarkdownTheme : MarkdownThemeOption.All[0]).Foreground;
    public Brush TextBorderBrush => (IsMarkdown ? SelectedMarkdownTheme : MarkdownThemeOption.All[0]).Border;
    public string MarkdownSettingsMessage => _markdownSettingsMessage;
    public Visibility MarkdownSettingsMessageVisibility => _markdownSettingsMessage.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public MarkdownThemeOption SelectedMarkdownTheme
    {
        get => _selectedMarkdownTheme;
        set
        {
            if (value is null || value == _selectedMarkdownTheme || !MarkdownThemes.Contains(value)) return;
            _selectedMarkdownTheme = value;
            _markdownSettingsMessage = _markdownThemeSettings.TrySave(value.Theme) ? string.Empty :
                "테마를 저장하지 못했습니다. 현재 창에는 적용되지만 다음 실행에는 유지되지 않을 수 있습니다.";
            NotifyAllStatePropertiesChanged();
        }
    }

    public bool ShowMarkdownSource
    {
        get => _showMarkdownSource;
        set
        {
            if (value == _showMarkdownSource) return;
            _showMarkdownSource = value;
            NotifyAllStatePropertiesChanged();
        }
    }

    public bool IsPurVisible => _currentResult.Pur is not null;
    public Visibility PurVisibility => IsPurVisible ? Visibility.Visible : Visibility.Collapsed;
    public IReadOnlyList<PurImageEntry> PurImages => _currentResult.Pur?.Images ?? Array.Empty<PurImageEntry>();
    public string PurSummary => $"PureRef · 포함 이미지 {PurImages.Count:N0}개";
    public PurImageEntry? SelectedPurImage
    {
        get => _currentResult.PurImage;
        set
        {
            if (value is null || value == SelectedPurImage || _currentResult.Pur is not { } pur || !pur.Images.Contains(value)) return;
            ApplyResult(_contentService.LoadPurImage(pur, value));
        }
    }

    public DocumentSection? SelectedDocumentSection
    {
        get => _selectedDocumentSection;
        set
        {
            if (value == _selectedDocumentSection || value is null || !DocumentSections.Contains(value)) return;
            _selectedDocumentSection = value;
            NotifyAllStatePropertiesChanged();
            ViewResetRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public ArchiveEntryInfo? SelectedArchiveEntry
    {
        get => _selectedArchiveEntry;
        set
        {
            if (value == _selectedArchiveEntry || value is null || _currentResult.Archive is not { } archive || !archive.Entries.Contains(value)) return;
            _selectedArchiveEntry = value;
            ApplyResult(_contentService.LoadArchiveEntry(archive, value));
        }
    }

    public PreviewStateKind State => _currentResult.State;

    public bool IsEmptyState => State == PreviewStateKind.Empty;

    public bool IsImageVisible => State == PreviewStateKind.ImageLoaded && PreviewBitmap is not null;

    public bool IsNoticeVisible => State is PreviewStateKind.UnsupportedFormat or PreviewStateKind.Error;

    public Visibility EmptyStateVisibility => IsEmptyState ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ImageVisibility => IsImageVisible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OpenedFileVisibility => IsEmptyState ? Visibility.Collapsed : Visibility.Visible;
    public Visibility NavigationVisibility => CanNavigatePrevious || CanNavigateNext ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ConversionToolsVisibility => CanConvertLoadedFormat ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ViewerToolbarVisibility => IsImageVisible || CanNavigatePrevious || CanNavigateNext
        ? Visibility.Visible : Visibility.Collapsed;

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

    public bool CanNavigatePrevious => !IsArchiveVisible && !IsPurVisible && _navigationContext.CanMovePrevious;

    public bool CanNavigateNext => !IsArchiveVisible && !IsPurVisible && _navigationContext.CanMoveNext;

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

    public string CurrentFileNameDisplay => _currentResult.FileName ?? "선택된 파일 없음";
    public string CurrentFilePathDisplay => string.IsNullOrWhiteSpace(_currentResult.FilePath)
        ? "선택된 파일 없음"
        : _currentResult.FilePath! + (_currentResult.ArchiveEntryName is { } entry ? $" › {entry}" : "");

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

                var purPart = IsPurVisible ? $"PUR · {(SelectedPurImage?.Index ?? 0) + 1}/{PurImages.Count} · " : string.Empty;
                return $"{purPart}{baseSummary}{rotationPart}{viewPart}{folderPart}";
            }

            return State switch
            {
                PreviewStateKind.TextLoaded => $"{DocumentSections.Count}개 구역 · {FormatFileSize(_currentResult.FileSizeBytes)}",
                PreviewStateKind.RiveLoaded => $"Rive · {FormatFileSize(_currentResult.FileSizeBytes)}",
                PreviewStateKind.ArchiveLoaded => ArchiveSummary,
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
        _selectedArchiveEntry = null;
        return ApplyResult(result);
    }

    private PreviewResult ApplyResult(PreviewResult result)
    {
        _currentResult = result;
        _selectedDocumentSection = result.Sections.FirstOrDefault();
        _lastConversionResult = ImageConversionResult.None();
        _isFitToWindow = true;
        _zoomFactor = DefaultZoomFactor;
        _rotationDegrees = 0;

        if (result.State == PreviewStateKind.ImageLoaded && result.ImageFormat is { } loadedFormat)
        {
            _selectedConversionFormat = GetSuggestedTargetFormat(loadedFormat, _selectedConversionFormat);
        }

        RebuildPreviewBitmap();
        _navigationContext = IsArchiveVisible || IsPurVisible || State is PreviewStateKind.TextLoaded or PreviewStateKind.RiveLoaded
            ? FolderNavigationContext.Empty : _folderNavigationService.ResolveContext(result.FilePath);

        NotifyAllStatePropertiesChanged();
        ViewResetRequested?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public ImageConversionResult ConvertCurrentImage(Window? owner = null)
    {
        if (IsArchiveVisible || IsPurVisible || State is PreviewStateKind.TextLoaded or PreviewStateKind.RiveLoaded)
            return BlockContentConversion(null, _selectedConversionFormat);
        if (IsEmptyState)
        {
            return ImageConversionResult.None();
        }

        if (State == PreviewStateKind.Error)
        {
            var errorBlocked = ImageConversionResult.Failure(
                _currentResult.FilePath,
                outputFilePath: null,
                sourceFormat: _currentResult.ImageFormat,
                targetFormat: _selectedConversionFormat,
                title: "열기에 실패한 이미지는 변환할 수 없습니다.",
                message: $"원본 파일을 정상적으로 읽을 수 없어 변환을 실행하지 않았습니다. {_currentResult.Message}");
            _lastConversionResult = errorBlocked;
            NotifyAllStatePropertiesChanged();
            return errorBlocked;
        }

        var precheck = _imageConversionService.ValidateCanConvertSource(
            _currentResult.FilePath,
            _currentResult.ImageFormat);
        if (!precheck.IsSuccess)
        {
            var blocked = precheck with { TargetFormat = _selectedConversionFormat };
            _lastConversionResult = blocked;
            NotifyAllStatePropertiesChanged();
            return blocked;
        }

        var suggestedPath = _imageConversionService.BuildSuggestedOutputPath(
            precheck.SourceFilePath!,
            _selectedConversionFormat);
        var destinationPath = _saveFileDialogService.PickSaveFile(
            suggestedPath,
            _selectedConversionFormat,
            owner);
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return ImageConversionResult.None();
        }

        var targetFormat = _selectedConversionFormat;
        var pickedExtension = Path.GetExtension(destinationPath.Trim().Trim('"'));
        if (SupportedFileFormats.TryGetConvertibleFormatByExtension(pickedExtension, out var extFormat))
        {
            targetFormat = extFormat;
            _selectedConversionFormat = extFormat;
        }

        return ConvertCurrentImageTo(destinationPath, targetFormat);
    }

    public ImageConversionResult ConvertCurrentImageTo(
        string? destinationFilePath,
        ImageFormatKind? targetFormat = null)
    {
        var effectiveTarget = targetFormat ?? _selectedConversionFormat;
        if (IsArchiveVisible || IsPurVisible || State is PreviewStateKind.TextLoaded or PreviewStateKind.RiveLoaded)
            return BlockContentConversion(destinationFilePath, effectiveTarget);
        if (SupportedFileFormats.IsConvertibleFormat(effectiveTarget))
        {
            _selectedConversionFormat = effectiveTarget;

            if (State == PreviewStateKind.Error)
            {
                var errorBlocked = ImageConversionResult.Failure(
                    _currentResult.FilePath,
                    destinationFilePath,
                    _currentResult.ImageFormat,
                    effectiveTarget,
                    title: "열기에 실패한 이미지는 변환할 수 없습니다.",
                    message: $"원본 파일을 정상적으로 읽을 수 없어 변환을 실행하지 않았습니다. {_currentResult.Message}");
                _lastConversionResult = errorBlocked;
                NotifyAllStatePropertiesChanged();
                return errorBlocked;
            }

            if (_currentResult.ImageFormat is { } knownFormat && !SupportedFileFormats.IsConvertibleFormat(knownFormat))
            {
                var unsupportedLoaded = _imageConversionService.ValidateCanConvertSource(
                    _currentResult.FilePath,
                    knownFormat) with
                {
                    OutputFilePath = destinationFilePath,
                    TargetFormat = effectiveTarget,
                };
                _lastConversionResult = unsupportedLoaded;
                NotifyAllStatePropertiesChanged();
                return unsupportedLoaded;
            }
        }

        var conversionResult = _imageConversionService.Convert(
            _currentResult.FilePath,
            effectiveTarget,
            destinationFilePath);
        _lastConversionResult = conversionResult;

        if (conversionResult.IsSuccess)
        {
            _navigationContext = _folderNavigationService.ResolveContext(_currentResult.FilePath);
        }

        NotifyAllStatePropertiesChanged();
        return conversionResult;
    }

    public bool OpenConvertedFile()
    {
        if (!CanOpenConvertedFile || string.IsNullOrWhiteSpace(_lastConversionResult.OutputFilePath))
        {
            return false;
        }

        var outputPath = _lastConversionResult.OutputFilePath;
        var opened = OpenFile(outputPath);
        return opened.State == PreviewStateKind.ImageLoaded;
    }

    public void DismissConversionNotice()
    {
        if (!HasConversionNotice)
        {
            return;
        }

        _lastConversionResult = ImageConversionResult.None();
        NotifyAllStatePropertiesChanged();
    }

    public bool MoveNextImage()
    {
        if (IsArchiveVisible || IsPurVisible || IsTextVisible || IsRiveVisible) return false;
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
        if (IsArchiveVisible || IsPurVisible || IsTextVisible || IsRiveVisible) return false;
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

    private static Win32FileDialogService CreateDefaultDialogService() => new();

    private ImageConversionResult BlockContentConversion(string? destination, ImageFormatKind format)
    {
        _lastConversionResult = ImageConversionResult.Unsupported(_currentResult.FilePath, destination, null, format,
            "이 미리보기는 이미지 변환을 지원하지 않습니다.", "ZIP 내부 항목·문서·텍스트·Rive·PureRef 파일은 내용 확인만 지원합니다. 변환하려면 별도의 원본 이미지 파일을 열어 주세요.");
        NotifyAllStatePropertiesChanged();
        return _lastConversionResult;
    }

    private static ImageFormatKind GetSuggestedTargetFormat(
        ImageFormatKind loadedFormat,
        ImageFormatKind fallbackFormat) => loadedFormat switch
    {
        ImageFormatKind.Jpeg => ImageFormatKind.Png,
        ImageFormatKind.Png => ImageFormatKind.Jpeg,
        ImageFormatKind.Bmp => ImageFormatKind.Png,
        _ => SupportedFileFormats.IsConvertibleFormat(fallbackFormat) ? fallbackFormat : ImageFormatKind.Png,
    };

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
        _convertImageCommand.RaiseCanExecuteChanged();
        _openConvertedFileCommand.RaiseCanExecuteChanged();
        _dismissConversionNoticeCommand.RaiseCanExecuteChanged();
    }
}
