using System;
using System.Threading;
using System.Windows;
using OKXMonitor.Services;

// Disambiguate: WinForms also has Application; pin to WPF.
using Application = System.Windows.Application;

namespace OKXMonitor;

public partial class App : Application
{
    Mutex? _mutex;
    Store? _store;
    AppSettings? _settings;
    TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(initiallyOwned: true, "OKXMonitor.SingleInstance", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        _settings = AppSettings.Load();
        _store = new Store(_settings);

        var window = new MainWindow(_store, _settings);
        window.Show();

        _tray = new TrayIcon();
        _tray.ToggleWindowRequested += () => window.ToggleVisibility();
        _tray.ToggleLayoutRequested += () => window.ToggleLayoutPublic();
        _tray.RefreshRequested += () => window.RefreshPublic();
        _tray.SettingsRequested += () => window.OpenSettingsPublic();
        _tray.QuitRequested += () => Shutdown();
        _store.PropertyChanged += (_, _) => Dispatcher.Invoke(() => _tray.Update(_store));
        _tray.Update(_store);

        _store.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
