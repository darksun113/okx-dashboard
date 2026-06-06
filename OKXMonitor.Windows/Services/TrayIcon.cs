using System;
using System.Drawing;
using System.Windows.Forms;
using OKXMonitor.Utils;

namespace OKXMonitor.Services;

/// System-tray icon showing live uPnL; right-click menu mirrors the macOS dropdown.
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon = new();
    readonly ContextMenuStrip _menu;
    Icon? _current;

    public event Action? ToggleWindowRequested;
    public event Action? ToggleLayoutRequested;
    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    public TrayIcon()
    {
        _menu = new ContextMenuStrip();
        _menu.Items.Add("显示/隐藏窗口", null, (_, _) => ToggleWindowRequested?.Invoke());
        _menu.Items.Add("切换布局", null, (_, _) => ToggleLayoutRequested?.Invoke());
        _menu.Items.Add("立即刷新", null, (_, _) => RefreshRequested?.Invoke());
        _menu.Items.Add("设置…", null, (_, _) => SettingsRequested?.Invoke());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => QuitRequested?.Invoke());
        _icon.ContextMenuStrip = _menu;
        _icon.Visible = true;
        _icon.DoubleClick += (_, _) => ToggleWindowRequested?.Invoke();
        Set(" OKX", Color.Gray, "OKX Monitor");
    }

    /// Update the icon from store state. Call on the UI thread after each refresh.
    public void Update(Store store)
    {
        if (store.NeedsSetup) { Set("OKX", Color.Gray, "未配置 API 凭据"); return; }
        if (store.LastUpdated is null && store.ErrorMessage is not null) { Set("--", Color.Gray, store.ErrorMessage); return; }
        double pnl = store.TotalUpl;
        var color = pnl > 0 ? Color.FromArgb(0x34, 0xC7, 0x59) : pnl < 0 ? Color.FromArgb(0xFF, 0x3B, 0x30) : Color.White;
        string text = (pnl > 0 ? "+" : "") + pnl.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        Set(text, color, "未实现盈亏 " + Format.SignedMoney(pnl));
    }

    void Set(string text, Color color, string tip)
    {
        var next = TrayIconRenderer.Render(text, color);
        _icon.Icon = next;
        _current?.Dispose();
        _current = next;
        _icon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip; // NotifyIcon tooltip cap
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _current?.Dispose();
    }
}
