using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using OKXMonitor.Interop;
using OKXMonitor.Services;
using OKXMonitor.Utils;

namespace OKXMonitor;

public partial class MainWindow : Window
{
    readonly Store _store;
    readonly AppSettings _settings;
    readonly Views.PortraitView _portrait = new();

    public MainWindow(Store store, AppSettings settings)
    {
        InitializeComponent();
        BodyHost.Content = _portrait;
        _store = store;
        _settings = settings;
        _store.PropertyChanged += (_, _) => Dispatcher.Invoke(Render);
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.HideFromAltTab(hwnd);
        RestoreGeometry();
        Render();
    }

    void RestoreGeometry()
    {
        var g = _settings.GeometryFor(LayoutKind.Portrait);
        if (g.Left is { } l && g.Top is { } t)
        {
            Left = l; Top = t;
            EnsureOnScreen();
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 20;
            Top = area.Top + 20;
        }
    }

    void EnsureOnScreen()
    {
        double vsLeft = SystemParameters.VirtualScreenLeft;
        double vsTop = SystemParameters.VirtualScreenTop;
        double vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
        double vsBottom = vsTop + SystemParameters.VirtualScreenHeight;
        if (Left < vsLeft || Left > vsRight - 50) Left = SystemParameters.WorkArea.Right - Width - 20;
        if (Top < vsTop || Top > vsBottom - 50) Top = SystemParameters.WorkArea.Top + 20;
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        var g = _settings.Portrait;
        g.Left = Left; g.Top = Top; g.Width = Width; g.Height = Height;
        _settings.Save();
    }

    void Render()
    {
        DemoBadge.Visibility = _store.IsDemo ? Visibility.Visible : Visibility.Collapsed;
        SetupPrompt.Visibility = _store.NeedsSetup ? Visibility.Visible : Visibility.Collapsed;
        BodyHost.Visibility = _store.NeedsSetup ? Visibility.Collapsed : Visibility.Visible;
        if (!_store.NeedsSetup) _portrait.Bind(_store);

        IntervalText.Text = $"{(int)_store.RefreshInterval}s";
        StatusText.Text = _store.ErrorMessage is { } err ? "⚠ " + err
            : _store.LastUpdated is { } t ? "更新于 " + t.ToString("HH:mm:ss")
            : "等待数据…";
        StatusText.Foreground = _store.ErrorMessage is null
            ? new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
    }

    void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await _store.RefreshAsync();

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Views.SettingsWindow(_store, _settings) { Owner = this };
        dlg.ShowDialog();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
