using System;
using System.Threading;
using System.Windows;
using OKXMonitor.Services;

namespace OKXMonitor;

public partial class App : Application
{
    Mutex? _mutex;
    Store? _store;
    AppSettings? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(initiallyOwned: true, "OKXMonitor.SingleInstance", out bool isNew);
        if (!isNew)
        {
            // Another instance already runs; just exit (it owns the widget + API load).
            Shutdown();
            return;
        }

        _settings = AppSettings.Load();
        _store = new Store(_settings);

        var window = new MainWindow(_store, _settings);
        window.Show();
        _store.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
