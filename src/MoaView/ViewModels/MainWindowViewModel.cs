using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MoaView.Models;
using MoaView.Services;

namespace MoaView.ViewModels;

/// <summary>
/// 메인 미리보기 창의 상태와 파일 선택·열기 흐름을 관리하는 뷰모델입니다.
/// </summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IPreviewService _previewService;
    private readonly IFileDialogService _fileDialogService;
    private PreviewResult _currentResult = PreviewResult.Empty();

    public MainWindowViewModel()
        : this(new ImagePreviewService(), new Win32FileDialogService())
    {
    }

    public MainWindowViewModel(IPreviewService previewService, IFileDialogService fileDialogService)
    {
        ArgumentNullException.ThrowIfNull(previewService);
        ArgumentNullException.ThrowIfNull(fileDialogService);

        _previewService = previewService;
        _fileDialogService = fileDialogService;
        OpenFileCommand = new RelayCommand(param => SelectAndOpenFile(param as Window));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand OpenFileCommand { get; }

    public PreviewResult CurrentResult
    {
        get => _currentResult;
        private set
        {
            if (ReferenceEquals(_currentResult, value))
            {
                return;
            }

            _currentResult = value;
            NotifyAllStatePropertiesChanged();
        }
    }

    public PreviewStateKind State => _currentResult.State;

    public bool IsEmptyState => State == PreviewStateKind.Empty;

    public bool IsImageVisible => State == PreviewStateKind.ImageLoaded && PreviewBitmap is not null;

    public bool IsNoticeVisible => State is PreviewStateKind.UnsupportedFormat or PreviewStateKind.Error;

    public Visibility EmptyStateVisibility => IsEmptyState ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ImageVisibility => IsImageVisible ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoticeVisibility => IsNoticeVisible ? Visibility.Visible : Visibility.Collapsed;

    public BitmapSource? PreviewBitmap => State == PreviewStateKind.ImageLoaded ? _currentResult.Bitmap : null;

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

    public string MetadataSummary => State switch
    {
        PreviewStateKind.ImageLoaded when _currentResult.ImageFormat is { } format =>
            $"{SupportedFileFormats.GetDisplayName(format)} · {_currentResult.PixelWidth} × {_currentResult.PixelHeight} px · {FormatFileSize(_currentResult.FileSizeBytes)}",
        PreviewStateKind.UnsupportedFormat => "미지원 형식",
        PreviewStateKind.Error => "미리보기 불가",
        _ => "대기 중",
    };

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
        CurrentResult = result;
        return result;
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
    }
}
