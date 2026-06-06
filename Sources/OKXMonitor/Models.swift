import Foundation

// MARK: - Generic OKX envelope

struct OKXResponse<T: Decodable>: Decodable {
    let code: String
    let msg: String
    let data: [T]?   // omitted entirely on auth errors (50xxx)
}

/// Lightweight envelope used to surface code/msg even when `data` is absent.
struct OKXStatus: Decodable {
    let code: String
    let msg: String
}

// MARK: - Account balance (/api/v5/account/balance)

struct AccountBalance: Decodable {
    let totalEq: String?      // total equity in USD
    let isoEq: String?        // isolated margin equity
    let adjEq: String?        // adjusted equity
    let mgnRatio: String?     // maintenance margin ratio
    let ordFroz: String?      // margin frozen for open orders
    let imr: String?          // initial margin requirement
    let mmr: String?          // maintenance margin requirement
    let details: [BalanceDetail]?
}

struct BalanceDetail: Decodable {
    let ccy: String?
    let eq: String?
    let availBal: String?
    let availEq: String?
    let upl: String?
}

// MARK: - Candles (/api/v5/market/candles)

/// One K-line bar. OKX returns each bar as a JSON array of strings; we only
/// keep the close price (index 4 in the [ts, o, h, l, c, vol, …] tuple).
struct Candle: Decodable {
    let close: Double

    init(from decoder: Decoder) throws {
        var c = try decoder.unkeyedContainer()
        _ = try c.decode(String.self) // ts
        _ = try c.decode(String.self) // open
        _ = try c.decode(String.self) // high
        _ = try c.decode(String.self) // low
        let cl = try c.decode(String.self)
        self.close = Double(cl) ?? 0
    }
}

/// Auto-derived watchlist entry: current price plus percent change over a few
/// recent windows. Built from a single 25×1H candle fetch per instrument.
struct WatchedToken: Identifiable {
    let instId: String
    let price: Double
    let change2h: Double?    // percent
    let change6h: Double?
    let change24h: Double?

    var id: String { instId }

    /// `candles` is newest-first (OKX default). Index N ≈ the close of the bar
    /// that ended N hours ago, so 24h% uses `candles[24]` as a baseline.
    static func from(instId: String, candles: [Candle]) -> WatchedToken? {
        guard let first = candles.first, first.close > 0 else { return nil }
        let now = first.close
        func change(_ n: Int) -> Double? {
            guard candles.indices.contains(n), candles[n].close > 0 else { return nil }
            return (now - candles[n].close) / candles[n].close * 100
        }
        return WatchedToken(
            instId: instId, price: now,
            change2h: change(2), change6h: change(6), change24h: change(24))
    }
}

// MARK: - Trade fee rate (/api/v5/account/trade-fee)

/// Account fee tier. Rates are negative fractions when a fee is charged
/// (e.g. "-0.0005" == 0.05%). The `*U` variants apply to USDT-margined
/// contracts; plain `taker`/`maker` apply to other (incl. coin-margined).
struct TradeFee: Decodable {
    let level: String?
    let taker: String?
    let maker: String?
    let takerU: String?
    let makerU: String?

    /// Taker rate as a positive percent, preferring the USDT-margined value.
    var takerPct: Double? { Self.pct(takerU ?? taker) }
    /// Maker rate as a positive percent, preferring the USDT-margined value.
    var makerPct: Double? { Self.pct(makerU ?? maker) }

    private static func pct(_ s: String?) -> Double? {
        guard let s, let v = Double(s) else { return nil }
        return abs(v) * 100
    }
}

// MARK: - Instrument spec (/api/v5/public/instruments)

/// Public contract spec; we only need the contract value to turn a filled
/// size (in contracts) into a notional amount.
struct InstrumentSpec: Decodable {
    let instId: String?
    let ctVal: String?        // contract value (face value per contract)
}

// MARK: - Positions (/api/v5/account/positions)

struct Position: Decodable, Identifiable {
    let instId: String?
    let posSide: String?      // long / short / net
    let pos: String?          // position size (contracts)
    let avgPx: String?        // average entry price
    let markPx: String?       // mark price
    let last: String?         // last traded price
    let upl: String?          // unrealized PnL
    let uplRatio: String?     // unrealized PnL ratio
    let lever: String?        // leverage
    let liqPx: String?        // estimated liquidation price
    let margin: String?       // margin used
    let mgnMode: String?      // cross / isolated
    let ccy: String?          // margin currency

    var id: String { (instId ?? "?") + "-" + (posSide ?? "net") }
}

// MARK: - Historical orders (/api/v5/trade/orders-history-archive)

/// A completed contract order. For close (平仓) orders OKX populates `pnl` with
/// the realized profit/loss of that fill; opening orders carry `pnl` == "0".
struct HistoryOrder: Decodable {
    let ordId: String?
    let instId: String?
    let pnl: String?          // gross realized PnL of this order (excl. fees)
    let fee: String?          // trading fee; negative = charged, positive = rebate
    let accFillSz: String?    // accumulated filled size (contracts)
    let avgPx: String?        // average fill price
    let state: String?        // filled / canceled
    let side: String?         // buy / sell
    let posSide: String?      // long / short / net
    let fillTime: String?     // last fill time (ms)
    let uTime: String?        // update time (ms)
    let cTime: String?        // creation time (ms)

    /// Gross realized PnL as a number (0 when absent/unparseable).
    var pnlValue: Double { Double(pnl ?? "") ?? 0 }

    /// Trading fee as a number; OKX reports a negative value when charged.
    var feeValue: Double { Double(fee ?? "") ?? 0 }

    /// Net realized PnL for this order, fees included (pnl + fee).
    var netPnl: Double { pnlValue + feeValue }

    /// Timestamp (ms) used to bucket this order into a period — the moment the
    /// trade actually filled, falling back to update / creation time.
    var eventTimeMs: Double? {
        for s in [fillTime, uTime, cTime] {
            if let s, let v = Double(s), v > 0 { return v }
        }
        return nil
    }
}

/// Aggregated realized PnL over a set of orders within a time window.
struct PnLStats {
    var profit: Double = 0   // sum of gross winning closes (pnl > 0)
    var loss: Double = 0     // sum of gross losing closes (pnl < 0), a ≤ 0 value
    var fee: Double = 0      // sum of all fees, open + close (≤ 0 when charged)
    var net: Double { profit + loss + fee }

    init() {}

    /// Build stats from `orders` whose fill time is at or after `startMs`.
    /// Wins/losses use gross PnL; fees (from every order — opening and closing)
    /// are accumulated separately, so `net = profit + loss + fee`.
    init(orders: [HistoryOrder], since startMs: Double) {
        for o in orders {
            guard let t = o.eventTimeMs, t >= startMs else { continue }
            let p = o.pnlValue
            if p > 0 { profit += p } else if p < 0 { loss += p }
            fee += o.feeValue
        }
    }
}

// MARK: - Pending orders (/api/v5/trade/orders-pending)

struct PendingOrder: Decodable, Identifiable {
    let ordId: String?
    let instId: String?
    let side: String?         // buy / sell
    let posSide: String?      // long / short / net
    let ordType: String?      // limit / post_only / fok / ioc ...
    let px: String?           // price
    let sz: String?           // size
    let fillSz: String?       // filled size
    let state: String?        // live / partially_filled
    let cTime: String?        // creation time (ms)
    let lever: String?

    var id: String { ordId ?? UUID().uuidString }
}
