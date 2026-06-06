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

    public System.Collections.Generic.List<PendingOrder> Orders { get; private set; } = new();
    public PnLStats PnlToday { get; private set; }
    public PnLStats PnlWeek { get; private set; }
    public PnLStats PnlMonth { get; private set; }
    public TradeFee? TradeFee { get; private set; }
    public double? EffectiveFeePct { get; private set; }
    public System.Collections.Generic.List<WatchedToken> Watchlist { get; private set; } = new();

    System.Collections.Generic.Dictionary<string, double> _ctValCache = new();
    bool _refreshing;

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
        if (_refreshing) return;
        _refreshing = true;
        IsLoading = true; Raise(nameof(IsLoading));
        var client = new OkxClient(_creds, _settings.Host);
        try
        {
            var (todayMs, weekMs, monthMs) = Utils.PeriodCalculator.PeriodStartsMs(DateTime.UtcNow);

            // Reference data — fetch once, never block the core refresh.
            if (TradeFee is null)
                try { TradeFee = await client.TradeFeeAsync(); } catch { }
            if (_ctValCache.Count == 0)
                try
                {
                    var insts = await client.InstrumentsAsync();
                    _ctValCache = insts
                        .Where(s => s.InstId is not null && double.TryParse(s.CtVal,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out _))
                        .GroupBy(s => s.InstId!)
                        .ToDictionary(g => g.Key, g => double.Parse(g.First().CtVal!,
                            System.Globalization.CultureInfo.InvariantCulture));
                }
                catch { }

            var balTask = client.BalanceAsync();
            var posTask = client.PositionsAsync();
            var ordTask = client.PendingOrdersAsync();
            var histTask = client.OrdersHistoryAsync(monthMs);
            await Task.WhenAll(balTask, posTask, ordTask, histTask);

            Balance = balTask.Result;
            Positions = posTask.Result.Where(p => p.PosValue != 0).ToList();
            Orders = ordTask.Result;
            var hist = histTask.Result;

            PnlToday = PnLStats.From(hist, todayMs);
            PnlWeek = PnLStats.From(hist, weekMs);
            PnlMonth = PnLStats.From(hist, monthMs);
            EffectiveFeePct = Utils.FeeCalculator.EffectiveFeePct(hist, monthMs, _ctValCache);

            var watchIds = Positions.Select(p => p.InstId)
                .Concat(Orders.Select(o => o.InstId))
                .Where(id => id is not null).Select(id => id!)
                .Distinct().ToList();
            Watchlist = await FetchWatchlistAsync(client, watchIds);

            LastUpdated = DateTime.Now;
            ErrorMessage = null;
        }
        catch (Exception e)
        {
            ErrorMessage = e is OkxException ox ? ox.Message : e.Message;
        }
        finally
        {
            _refreshing = false;
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
                 nameof(IsLoading), nameof(ErrorMessage), nameof(LastUpdated), nameof(NeedsSetup),
                 nameof(Orders), nameof(PnlToday), nameof(PnlWeek), nameof(PnlMonth),
                 nameof(TradeFee), nameof(EffectiveFeePct), nameof(Watchlist) })
            Raise(n);
    }

    static async Task<System.Collections.Generic.List<WatchedToken>> FetchWatchlistAsync(
        OkxClient client, System.Collections.Generic.List<string> instIds)
    {
        var results = new WatchedToken?[instIds.Count];
        var opts = new ParallelOptions { MaxDegreeOfParallelism = 6 };
        await Parallel.ForEachAsync(Enumerable.Range(0, instIds.Count), opts, async (i, ct) =>
        {
            try
            {
                var candles = await client.Candles1HAsync(instIds[i]);
                results[i] = WatchedToken.From(instIds[i], candles);
            }
            catch { results[i] = null; }
        });
        return results.Where(t => t is not null).Select(t => t!).ToList();
    }
}
