using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OKXMonitor.Models;

namespace OKXMonitor.Services;

/// Read-only OKX v5 REST client. Signs every request with HMAC-SHA256.
/// Port of Sources/OKXMonitor/OKXClient.swift — one method per endpoint, all
/// signing centralized here.
public sealed class OkxClient
{
    /// AppSettings key holding the OKX REST host (without scheme).
    public const string HostSettingKey = "okx_host";
    /// Default host. `.cab` is OKX's alternate domain for regions where the
    /// primary `.com` host is blocked or unreliable.
    public const string DefaultHost = "www.okx.cab";
    /// Hosts surfaced as quick picks in Settings.
    public static readonly string[] KnownHosts = { "www.okx.cab", "www.okx.com", "aws.okx.com" };

    // One shared client; certificate validation is left at the secure default.
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new CandleConverter() },
    };

    readonly Credentials _creds;
    readonly string _host;   // includes scheme, e.g. https://www.okx.cab

    public OkxClient(Credentials creds, string? host)
    {
        _creds = creds;
        var h = string.IsNullOrWhiteSpace(host) ? DefaultHost : host.Trim();
        _host = "https://" + h;
    }

    static string Sign(string secret, string timestamp, string method, string path, string body = "")
    {
        var prehash = timestamp + method + path + body;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(prehash));
        return Convert.ToBase64String(mac);
    }

    // ISO-8601 UTC with milliseconds, e.g. 2026-06-06T03:14:15.123Z
    static string TimestampUtc() =>
        DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    async Task<List<T>> RequestAsync<T>(string path)
    {
        if (!_creds.IsComplete) throw OkxException.MissingCredentials();
        var timestamp = TimestampUtc();
        var signature = Sign(_creds.SecretKey, timestamp, "GET", path, "");

        using var req = new HttpRequestMessage(HttpMethod.Get, _host + path);
        req.Headers.TryAddWithoutValidation("OK-ACCESS-KEY", _creds.ApiKey);
        req.Headers.TryAddWithoutValidation("OK-ACCESS-SIGN", signature);
        req.Headers.TryAddWithoutValidation("OK-ACCESS-TIMESTAMP", timestamp);
        req.Headers.TryAddWithoutValidation("OK-ACCESS-PASSPHRASE", _creds.Passphrase);
        if (_creds.Demo) req.Headers.TryAddWithoutValidation("x-simulated-trading", "1");

        HttpResponseMessage resp;
        string text;
        try
        {
            resp = await Http.SendAsync(req).ConfigureAwait(false);
            text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Network failure, DNS, TLS handshake, or the 15s timeout.
            throw OkxException.Transport(path, e);
        }

        if (!resp.IsSuccessStatusCode)
        {
            // OKX returns a JSON body with code/msg on auth errors (often without
            // `data`); surface it in preference to a bare HTTP status.
            try
            {
                var status = JsonSerializer.Deserialize<OkxStatus>(text, JsonOpts);
                if (status is not null && status.Code != "0")
                    throw OkxException.Api(status.Code, status.Msg);
            }
            catch (JsonException) { /* fall through to HTTP error */ }
            throw OkxException.Http(path, (int)resp.StatusCode);
        }

        var env = JsonSerializer.Deserialize<OkxResponse<T>>(text, JsonOpts);
        if (env is null) throw OkxException.Http(path, (int)resp.StatusCode);
        if (env.Code != "0") throw OkxException.Api(env.Code, env.Msg);
        return env.Data ?? new List<T>();
    }

    // MARK: - Endpoints

    public async Task<AccountBalance?> BalanceAsync() =>
        (await RequestAsync<AccountBalance>("/api/v5/account/balance")).FirstOrDefault();

    public Task<List<Position>> PositionsAsync() =>
        // SWAP + FUTURES contracts only.
        RequestAsync<Position>("/api/v5/account/positions?instType=SWAP");

    public Task<List<PendingOrder>> PendingOrdersAsync() =>
        RequestAsync<PendingOrder>("/api/v5/trade/orders-pending");

    /// Recent 1-hour candles for an instrument. Public endpoint; we still send the
    /// signed headers, which OKX harmlessly ignores on public routes.
    public Task<List<Candle>> Candles1HAsync(string instId, int limit = 25)
    {
        var id = Uri.EscapeDataString(instId);
        return RequestAsync<Candle>($"/api/v5/market/candles?instId={id}&bar=1H&limit={limit}");
    }

    /// Current account trade-fee tier (maker/taker rates).
    public async Task<TradeFee?> TradeFeeAsync(string instType = "SWAP") =>
        (await RequestAsync<TradeFee>($"/api/v5/account/trade-fee?instType={instType}")).FirstOrDefault();

    /// Public contract specs (for contract values).
    public Task<List<InstrumentSpec>> InstrumentsAsync(string instType = "SWAP") =>
        RequestAsync<InstrumentSpec>($"/api/v5/public/instruments?instType={instType}");

    /// All completed SWAP/FUTURES orders since `beginMs`, deduped by ordId.
    ///
    /// Merges the 7-day `orders-history` (real-time, so the latest closes — and
    /// thus 今日/本周 — are never missed) with the 3-month `orders-history-archive`
    /// (covers the rest of month-to-date). When the requested window is within
    /// 7 days, the archive call is skipped. The real-time copy wins on duplicates.
    public async Task<List<HistoryOrder>> OrdersHistoryAsync(double beginMs, string instType = "SWAP")
    {
        double nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        double sevenDaysAgoMs = nowMs - 7d * 86_400 * 1000;

        // Always pull the 7-day real-time endpoint (covers today / this week).
        var recentTask = PaginatedOrdersAsync("orders-history", instType, oldestNeededMs: beginMs);
        // Only walk the 3-month archive if we need orders older than 7 days.
        var archivedTask = beginMs < sevenDaysAgoMs
            ? PaginatedOrdersAsync("orders-history-archive", instType, oldestNeededMs: beginMs)
            : Task.FromResult(new List<HistoryOrder>());

        await Task.WhenAll(recentTask, archivedTask).ConfigureAwait(false);

        // Insert archived first, then recent, so the real-time copy wins for any
        // order returned by both endpoints.
        var byId = new Dictionary<string, HistoryOrder>();
        foreach (var o in archivedTask.Result) if (o.OrdId is { } id) byId[id] = o;
        foreach (var o in recentTask.Result) if (o.OrdId is { } id) byId[id] = o;
        return byId.Values.ToList();
    }

    /// Pages backward via the `after` (ordId) cursor until everything newer than
    /// `oldestNeededMs` is covered.
    ///
    /// We intentionally do NOT pass OKX's `begin` query parameter: empirically it
    /// can drop orders within the window (page 1 returning only a fraction of the
    /// day's fills), which made today's wins vanish and 今日 盈利 show 0. Paging by
    /// `after` and stopping when a page's oldest eventTime falls below the threshold
    /// is exhaustive. Mirrors OKXClient.swift's paginatedOrders.
    async Task<List<HistoryOrder>> PaginatedOrdersAsync(string endpoint, string instType, double oldestNeededMs)
    {
        var all = new List<HistoryOrder>();
        string? after = null;
        // Safety cap: 30 pages × 100 = 3000 orders.
        for (int i = 0; i < 30; i++)
        {
            var path = $"/api/v5/trade/{endpoint}?instType={instType}&limit=100";
            if (after is not null) path += $"&after={after}";
            var page = await RequestAsync<HistoryOrder>(path).ConfigureAwait(false);
            all.AddRange(page);
            // Stop if the server has nothing more, OR we've paged past the oldest
            // order we care about.
            if (page.Count != 100 || page[^1].OrdId is not { } last) break;
            double oldestInPage = page
                .Select(o => o.EventTimeMs)
                .Where(t => t.HasValue)
                .Select(t => t!.Value)
                .DefaultIfEmpty(double.PositiveInfinity)
                .Min();
            if (oldestInPage < oldestNeededMs) break;
            after = last;
        }
        return all;
    }
}
