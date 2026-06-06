using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using OKXMonitor.Interop;
using OKXMonitor.Services;

// Disambiguate WinForms vs WPF types brought in by UseWindowsForms.
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace OKXMonitor;

public partial class MainWindow : Window
{
    readonly Store _store;
    readonly AppSettings _settings;
    readonly Views.PortraitView _portrait = new();
    readonly Views.LandscapeView _landscape = new();
    LayoutKind _current;

    static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
    static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));

    public MainWindow(Store store, AppSettings settings)
    {
        InitializeComponent();
        _store = store;
        _settings = settings;
        _current = _settings.LastLayout;
        _store.PropertyChanged += (_, _) => Dispatcher.Invoke(Render);
        Loaded += OnLoaded;
        Closing += OnClosing;
        ApplyLayout(_current, restore: false);
    }

    void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.HideFromAltTab(hwnd);
        RestoreGeometry(_current);
        Render();
    }

    void ApplyLayout(LayoutKind kind, bool restore)
    {
        _current = kind;
        if (kind == LayoutKind.Portrait)
        {
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            Width = 380;
            BodyHost.Content = _portrait;
        }
        else
        {
            SizeToContent = SizeToContent.Manual;
            ResizeMode = ResizeMode.CanResize;   // width draggable
            BodyHost.Content = _landscape;
        }
        if (restore) RestoreGeometry(kind);
        Render();
    }

    void RestoreGeometry(LayoutKind kind)
    {
        var g = _settings.GeometryFor(kind);
        if (kind == LayoutKind.Landscape)
        {
            Width = g.Width ?? 1440;
            Height = g.Height ?? 240;
        }
        if (g.Left is { } l && g.Top is { } t) { Left = l; Top = t; EnsureOnScreen(); }
        else { var a = SystemParameters.WorkArea; Left = a.Right - Width - 20; Top = a.Top + 20; }
    }

    void EnsureOnScreen()
    {
        double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
        double vr = vl + SystemParameters.VirtualScreenWidth, vb = vt + SystemParameters.VirtualScreenHeight;
        if (Left < vl || Left > vr - 50) Left = SystemParameters.WorkArea.Right - Width - 20;
        if (Top < vt || Top > vb - 50) Top = SystemParameters.WorkArea.Top + 20;
    }

    void SaveGeometry(LayoutKind kind)
    {
        var g = _settings.GeometryFor(kind);
        g.Left = Left; g.Top = Top; g.Width = Width; g.Height = Height;
        _settings.Save();
    }

    void OnClosing(object? sender, CancelEventArgs e) => SaveGeometry(_current);

    void Render()
    {
        DemoBadge.Visibility = _store.IsDemo ? Visibility.Visible : Visibility.Collapsed;
        SetupPrompt.Visibility = _store.NeedsSetup ? Visibility.Visible : Visibility.Collapsed;
        BodyHost.Visibility = _store.NeedsSetup ? Visibility.Collapsed : Visibility.Visible;

        if (!_store.NeedsSetup)
        {
            if (_current == LayoutKind.Portrait) _portrait.Bind(_store);
            else _landscape.Bind(_store);
        }

        IntervalText.Text = $"{(int)_store.RefreshInterval}s";
        StatusText.Text = _store.ErrorMessage is { } err ? "⚠ " + err
            : _store.LastUpdated is { } t ? "更新于 " + t.ToString("HH:mm:ss")
            : "等待数据…";
        StatusText.Foreground = _store.ErrorMessage is null ? Gray : Red;
    }

    void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await _store.RefreshAsync();

    void Toggle_Click(object sender, RoutedEventArgs e)
    {
        SaveGeometry(_current);
        var next = _current == LayoutKind.Portrait ? LayoutKind.Landscape : LayoutKind.Portrait;
        _settings.LastLayout = next;
        _settings.Save();
        ApplyLayout(next, restore: true);
    }

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Views.SettingsWindow(_store, _settings) { Owner = this };
        dlg.ShowDialog();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    // Public hooks for the tray icon.
    public void ToggleVisibility() { if (IsVisible) Hide(); else { Show(); Activate(); } }
    public void ToggleLayoutPublic() => Toggle_Click(this, new RoutedEventArgs());
    public void RefreshPublic() => _ = _store.RefreshAsync();
    public void OpenSettingsPublic() => Settings_Click(this, new RoutedEventArgs());
}
