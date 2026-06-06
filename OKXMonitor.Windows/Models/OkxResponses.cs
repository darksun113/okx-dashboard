using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OKXMonitor.Models;

// Mirrors Sources/OKXMonitor/Models.swift. OKX returns every numeric field as a
// string to preserve precision, so every value below is string? and parsed with
// InvariantCulture where needed.

// MARK: - Generic OKX envelope

public sealed class OkxResponse<T>
{
    public string Code { get; set; } = "";
    public string Msg { get; set; } = "";
    public List<T>? Data { get; set; }   // omitted entirely on auth errors (50xxx)
}

/// Lightweight envelope used to surface code/msg even when `data` is absent.
public sealed class OkxStatus
{
    public string Code { get; set; } = "";
    public string Msg { get; set; } = "";
}

// MARK: - Account balance (/api/v5/account/balance)

public sealed class AccountBalance
{
    public string? TotalEq { get; set; }   // total equity in USD
    public string? IsoEq { get; set; }     // isolated margin equity
    public string? AdjEq { get; set; }     // adjusted equity
    public string? MgnRatio { get; set; }  // maintenance margin ratio
    public string? OrdFroz { get; set; }   // margin frozen for open orders
    public string? Imr { get; set; }       // initial margin requirement
    public string? Mmr { get; set; }       // maintenance margin requirement
    public List<BalanceDetail>? Details { get; set; }
}

public sealed class BalanceDetail
{
    public string? Ccy { get; set; }
    public string? Eq { get; set; }
    public string? AvailBal { get; set; }
    public string? AvailEq { get; set; }
    public string? Upl { get; set; }
}

// MARK: - Candles (/api/v5/market/candles)

/// One K-line bar. OKX returns each bar as a JSON array of strings; we only keep
/// the close price (index 4 in the [ts, o, h, l, c, vol, …] tuple).
public sealed class Candle
{
    public double Close { get; init; }
}

/// Decodes a candle from OKX's array-of-strings form.
public sealed class CandleConverter : JsonConverter<Candle>
{
    public override Candle Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        if (r.TokenType != JsonTokenType.StartArray) throw new JsonException("candle not an array");
        r.Read(); // ts
        r.Read(); // open
        r.Read(); // high
        r.Read(); // low
        var cl = r.GetString();                 // close (index 4)
        double close = double.TryParse(cl, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
        while (r.TokenType != JsonTokenType.EndArray) r.Read();
        return new Candle { Close = close };
    }

    public override void Write(Utf8JsonWriter w, Candle v, JsonSerializerOptions o) =>
        throw new NotSupportedException();
}

/// Auto-derived watchlist entry: current price plus percent change over a few
/// recent windows. Built from a single 25×1H candle fetch per instrument.
public sealed class WatchedToken
{
    public required string InstId { get; init; }
    public double Price { get; init; }
    public double? Change2h { get; init; }   // percent
    public double? Change6h { get; init; }
    public double? Change24h { get; init; }

    /// `candles` is newest-first (OKX default). Index N ≈ the close of the bar
    /// that ended N hours ago, so 24h% uses candles[24] as a baseline.
    public static WatchedToken? From(string instId, IReadOnlyList<Candle> candles)
    {
        if (candles.Count == 0 || candles[0].Close <= 0) return null;
        double now = candles[0].Close;
        double? Change(int n)
        {
            if (n < 0 || n >= candles.Count || candles[n].Close <= 0) return null;
            return (now - candles[n].Close) / candles[n].Close * 100;
        }
        return new WatchedToken
        {
            InstId = instId,
            Price = now,
            Change2h = Change(2),
            Change6h = Change(6),
            Change24h = Change(24),
        };
    }
}

// MARK: - Trade fee rate (/api/v5/account/trade-fee)

/// Account fee tier. Rates are negative fractions when a fee is charged
/// (e.g. "-0.0005" == 0.05%). The `*U` variants apply to USDT-margined contracts.
public sealed class TradeFee
{
    public string? Level { get; set; }
    public string? Taker { get; set; }
    public string? Maker { get; set; }
    public string? TakerU { get; set; }
    public string? MakerU { get; set; }

    /// Taker rate as a positive percent, preferring the USDT-margined value.
    public double? TakerPct => Pct(TakerU ?? Taker);
    /// Maker rate as a positive percent, preferring the USDT-margined value.
    public double? MakerPct => Pct(MakerU ?? Maker);

    static double? Pct(string? s) =>
        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? Math.Abs(v) * 100 : null;
}

// MARK: - Instrument spec (/api/v5/public/instruments)

/// Public contract spec; we only need the contract value to turn a filled size
/// (in contracts) into a notional amount.
public sealed class InstrumentSpec
{
    public string? InstId { get; set; }
    public string? CtVal { get; set; }   // contract value (face value per contract)
}

// MARK: - Positions (/api/v5/account/positions)

public sealed class Position
{
    public string? InstId { get; set; }
    public string? PosSide { get; set; }   // long / short / net
    public string? Pos { get; set; }       // position size (contracts)
    public string? AvgPx { get; set; }     // average entry price
    public string? MarkPx { get; set; }    // mark price
    public string? Last { get; set; }      // last traded price
    public string? Upl { get; set; }       // unrealized PnL
    public string? UplRatio { get; set; }  // unrealized PnL ratio
    public string? Lever { get; set; }     // leverage
    public string? LiqPx { get; set; }     // estimated liquidation price
    public string? Margin { get; set; }    // margin used
    public string? MgnMode { get; set; }   // cross / isolated
    public string? Ccy { get; set; }       // margin currency

    public string Id => (InstId ?? "?") + "-" + (PosSide ?? "net");

    /// Position size as a number (0 when absent/unparseable).
    public double PosValue => Num(Pos);
    public double UplValue => Num(Upl);

    static double Num(string? s) =>
        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
}

// MARK: - Historical orders (/api/v5/trade/orders-history[-archive])

/// A completed contract order. For close (平仓) orders OKX populates `pnl` with the
/// realized profit/loss of that fill; opening orders carry `pnl` == "0".
public sealed class HistoryOrder
{
    public string? OrdId { get; set; }
    public string? InstId { get; set; }
    public string? Pnl { get; set; }        // gross realized PnL of this order (excl. fees)
    public string? Fee { get; set; }        // trading fee; negative = charged, positive = rebate
    public string? AccFillSz { get; set; }  // accumulated filled size (contracts)
    public string? AvgPx { get; set; }      // average fill price
    public string? State { get; set; }      // filled / canceled
    public string? Side { get; set; }       // buy / sell
    public string? PosSide { get; set; }    // long / short / net
    public string? FillTime { get; set; }   // last fill time (ms)
    public string? UTime { get; set; }      // update time (ms)
    public string? CTime { get; set; }      // creation time (ms)

    /// Gross realized PnL as a number (0 when absent/unparseable).
    public double PnlValue => Num(Pnl);
    /// Trading fee as a number; OKX reports a negative value when charged.
    public double FeeValue => Num(Fee);
    /// Net realized PnL for this order, fees included (pnl + fee).
    public double NetPnl => PnlValue + FeeValue;

    /// Timestamp (ms) used to bucket this order into a period — the moment the
    /// trade actually filled, falling back to update / creation time.
    public double? EventTimeMs
    {
        get
        {
            foreach (var s in new[] { FillTime, UTime, CTime })
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v > 0)
                    return v;
            return null;
        }
    }

    static double Num(string? s) =>
        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
}

/// Aggregated realized PnL over a set of orders within a time window.
public readonly struct PnLStats
{
    public double Profit { get; init; }   // sum of gross winning closes (pnl > 0)
    public double Loss { get; init; }     // sum of gross losing closes (pnl < 0), a ≤ 0 value
    public double Fee { get; init; }      // sum of all fees, open + close (≤ 0 when charged)
    public double Net => Profit + Loss + Fee;

    /// Build stats from `orders` whose fill time is at or after `startMs`.
    /// Wins/losses use gross PnL; fees (from every order — opening and closing)
    /// are accumulated separately, so Net = Profit + Loss + Fee.
    public static PnLStats From(IEnumerable<HistoryOrder> orders, double startMs)
    {
        double profit = 0, loss = 0, fee = 0;
        foreach (var o in orders)
        {
            if (o.EventTimeMs is not { } t || t < startMs) continue;
            var p = o.PnlValue;
            if (p > 0) profit += p; else if (p < 0) loss += p;
            fee += o.FeeValue;
        }
        return new PnLStats { Profit = profit, Loss = loss, Fee = fee };
    }
}

// MARK: - Pending orders (/api/v5/trade/orders-pending)

public sealed class PendingOrder
{
    public string? OrdId { get; set; }
    public string? InstId { get; set; }
    public string? Side { get; set; }      // buy / sell
    public string? PosSide { get; set; }   // long / short / net
    public string? OrdType { get; set; }   // limit / post_only / fok / ioc ...
    public string? Px { get; set; }        // price
    public string? Sz { get; set; }        // size
    public string? FillSz { get; set; }    // filled size
    public string? State { get; set; }     // live / partially_filled
    public string? CTime { get; set; }     // creation time (ms)
    public string? Lever { get; set; }
}
