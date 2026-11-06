using MoaView.Models;

namespace MoaView.Services;

/// <summary>Shares the selected appearance between the app and its Explorer preview windows.</summary>
public sealed class AppAppearance
{
    internal static AppAppearance Current { get; } = new(new FileAppThemeSettings());
    private readonly IAppThemeSettings _settings;

    public AppAppearance(IAppThemeSettings settings)
    {
        _settings = settings;
        Theme = settings.Load();
    }

    public AppTheme Theme { get; private set; }
    public string SaveMessage { get; private set; } = string.Empty;
    public event EventHandler? ThemeChanged;

    public void Select(AppTheme theme)
    {
        if (theme == Theme || !Enum.IsDefined(theme)) return;
        Theme = theme;
        SaveMessage = _settings.TrySave(theme) ? string.Empty :
            "테마를 저장하지 못했습니다. 이번 실행 동안은 선택한 테마를 사용할 수 있어요.";
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }
}
