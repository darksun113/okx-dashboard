using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using OKXMonitor.Models;

namespace OKXMonitor.Services;

/// Shared observable state + polling timer. Phase A wires balance + positions only;
/// Phase B extends RefreshAsync with PnL/fees/watchlist. Port of Store.swift.
public sealed class Store : INotifyPropertyChanged
{
    readonly DispatcherTimer _timer = new();
    AppSettings _settings;
    Credentials _creds;

    public Store(AppSettings settings)
    {
        _settings = settings;
        _creds = CredentialStore.Load();
        NeedsSetup = !_creds.IsComplete;
        _timer.Tick += async (_, _) => await RefreshAsync();
        ApplyInterval();
    }

    public AccountBalance? Balance { get; private set; }
    public System.Collections.Generic.List<Position> Positions { get; private set; } = new();
    public bool NeedsSetup { get; private set; }
    public bool IsLoading { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime? LastUpdated { get; private set; }
    public bool IsDemo => _creds.Demo;
    public double RefreshInterval => _settings.RefreshInterval;

    public double TotalUpl => Positions.Sum(p => p.UplValue);

    public void Start()
    {
        if (NeedsSetup) return;
        _timer.Start();
        _ = RefreshAsync();
    }

    void ApplyInterval()
    {
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(2, _settings.RefreshInterval));
    }

    public void UpdateCredentials(Credentials c)
    {
        CredentialStore.Save(c);
        _creds = c;
        NeedsSetup = !c.IsComplete;
        ErrorMessage = null;
        Raise(nameof(NeedsSetup));
        Raise(nameof(IsDemo));
        if (!NeedsSetup) { ApplyInterval(); _timer.Start(); _ = RefreshAsync(); }
        else _timer.Stop();
    }

    public void UpdateSettings(AppSettings s)
    {
        _settings = s;
        ApplyInterval();
        Raise(nameof(RefreshInterval));
    }

    public async Task RefreshAsync()
    {
        if (NeedsSetup) return;
        IsLoading = true; Raise(nameof(IsLoading));
        var client = new OkxClient(_creds, _settings.Host);
        try
        {
            var balTask = client.BalanceAsync();
            var posTask = client.PositionsAsync();
            await Task.WhenAll(balTask, posTask);
            Balance = balTask.Result;
            Positions = posTask.Result.Where(p => p.PosValue != 0).ToList();
            LastUpdated = DateTime.Now;
            ErrorMessage = null;
        }
        catch (Exception e)
        {
            ErrorMessage = e is OkxException ox ? ox.Message : e.Message;
        }
        finally
        {
            IsLoading = false;
            RaiseAll();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    void RaiseAll()
    {
        foreach (var n in new[] { nameof(Balance), nameof(Positions), nameof(TotalUpl),
                 nameof(IsLoading), nameof(ErrorMessage), nameof(LastUpdated), nameof(NeedsSetup) })
            Raise(n);
    }
}
