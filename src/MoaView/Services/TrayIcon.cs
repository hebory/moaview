using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace MoaView.Services;

/// <summary>Owns the notification icon, including Explorer restart recovery provided by NotifyIcon.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Icon _icon;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _spaceItem;
    private bool _closeHintShown;

    public TrayIcon(Action open, Action toggleSpace, Action exit)
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/MoaView;component/Assets/Brand/MoaView.ico"))!.Stream;
        _icon = new Icon(stream);
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("MoaView 열기", null, (_, _) => open());
        _spaceItem = new Forms.ToolStripMenuItem("탐색기 Space 미리보기", null, (_, _) => toggleSpace());
        _menu.Items.Add(_spaceItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("MoaView 종료", null, (_, _) => exit());
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "MoaView",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => open();
        _notifyIcon.BalloonTipClicked += (_, _) => open();
    }

    public void SetExplorerState(bool enabled)
    {
        _spaceItem.Checked = enabled;
        _notifyIcon.Text = enabled ? "MoaView · 탐색기 Space 켜짐" : "MoaView · 탐색기 Space 꺼짐";
    }

    public void ShowCloseHint()
    {
        if (_closeHintShown) return;
        _closeHintShown = true;
        _notifyIcon.ShowBalloonTip(4000, "MoaView가 트레이에서 실행 중입니다",
            "트레이 메뉴에서 Space 미리보기를 켜고 끌 수 있어요. 완전히 끝내려면 ‘MoaView 종료’를 선택하세요.", Forms.ToolTipIcon.Info);
    }

    public void ShowError(string message) =>
        _notifyIcon.ShowBalloonTip(5000, "탐색기 Space 미리보기", message, Forms.ToolTipIcon.Warning);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}
