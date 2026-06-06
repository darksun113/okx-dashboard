import Foundation
import SwiftUI
import Combine

/// Observable state shared by the SwiftUI views. Polls OKX on a timer.
@MainActor
final class Store: ObservableObject {
    @Published var balance: AccountBalance?
    @Published var positions: [Position] = []
    @Published var orders: [PendingOrder] = []
    @Published var pnlToday = PnLStats()   // GMT+8 00:00 today → now
    @Published var pnlWeek = PnLStats()    // GMT+8 Monday 00:00 → now
    @Published var pnlMonth = PnLStats()   // GMT+8 1st 00:00 → now
    @Published var tradeFee: TradeFee?     // official maker/taker tier
    @Published var effectiveFeePct: Double?  // month: Σ|fee| / Σ notional, %
    @Published var watchlist: [WatchedToken] = []  // auto-derived from positions+orders
    @Published var lastUpdated: Date?
    @Published var errorMessage: String?
    @Published var isLoading = false
    @Published var needsSetup: Bool

    @Published var refreshInterval: Double {
        didSet {
            UserDefaults.standard.set(refreshInterval, forKey: "okx_refresh_interval")
            restartTimer()
        }
    }

    private var creds: Credentials
    private var timer: Timer?
    private var ctValCache: [String: Double] = [:]   // instId → contract value

    init() {
        self.creds = Credentials.load()
        self.needsSetup = !creds.isComplete
        let saved = UserDefaults.standard.double(forKey: "okx_refresh_interval")
        self.refreshInterval = saved >= 2 ? saved : 5
    }

    func start() {
        guard !needsSetup else { return }
        restartTimer()
        Task { await refresh() }
    }

    private func restartTimer() {
        timer?.invalidate()
        guard !needsSetup else { return }
        timer = Timer.scheduledTimer(withTimeInterval: refreshInterval, repeats: true) { [weak self] _ in
            Task { await self?.refresh() }
        }
    }

    func updateCredentials(_ new: Credentials) {
        new.save()
        creds = new
        needsSetup = !new.isComplete
        errorMessage = nil
        if !needsSetup {
            start()
        } else {
            timer?.invalidate()
        }
    }

    func refresh() async {
        guard !needsSetup else { return }
        isLoading = true
        defer { isLoading = false }
        let client = OKXClient(creds: creds)
        let periods = Self.periodStartsMs()
        // Reference data that rarely changes — fetch once, then reuse. `try?`
        // so a failure here never blocks the core position/PnL refresh.
        if tradeFee == nil { tradeFee = try? await client.tradeFee() }
        if ctValCache.isEmpty, let insts = try? await client.instruments() {
            ctValCache = Dictionary(
                insts.compactMap { spec in
                    guard let id = spec.instId, let cv = Double(spec.ctVal ?? "") else { return nil }
                    return (id, cv)
                },
                uniquingKeysWith: { first, _ in first })
        }
        do {
            async let b = client.balance()
            async let p = client.positions()
            async let o = client.pendingOrders()
            async let h = client.ordersHistory(beginMs: periods.month)
            let (bal, pos, ords, hist) = try await (b, p, o, h)
            self.balance = bal
            self.positions = pos.filter { ($0.pos.flatMap(Double.init) ?? 0) != 0 }
            self.orders = ords
            self.pnlToday = PnLStats(orders: hist, since: periods.today)
            self.pnlWeek = PnLStats(orders: hist, since: periods.week)
            self.pnlMonth = PnLStats(orders: hist, since: periods.month)
            self.effectiveFeePct = Self.effectiveFeePct(
                orders: hist, since: periods.month, ctVals: ctValCache)
            let watchIds = Self.uniqueOrdered(
                self.positions.compactMap { $0.instId } + self.orders.compactMap { $0.instId })
            self.watchlist = await Self.fetchWatchlist(client: client, instIds: watchIds)
            self.lastUpdated = Date()
            self.errorMessage = nil
        } catch {
            self.errorMessage = error.localizedDescription
        }
    }

    /// Deduplicate while preserving first-seen order.
    private static func uniqueOrdered(_ items: [String]) -> [String] {
        var seen = Set<String>()
        return items.filter { seen.insert($0).inserted }
    }

    /// Fetch 1H candles for each instId in parallel and assemble watchlist
    /// entries. Per-token failures are dropped silently — the row just won't
    /// appear that refresh.
    private static func fetchWatchlist(client: OKXClient, instIds: [String]) async -> [WatchedToken] {
        await withTaskGroup(of: (Int, WatchedToken?).self) { group in
            for (i, id) in instIds.enumerated() {
                group.addTask {
                    let candles = (try? await client.candles1H(instId: id)) ?? []
                    return (i, WatchedToken.from(instId: id, candles: candles))
                }
            }
            var collected: [(Int, WatchedToken)] = []
            for await (i, t) in group {
                if let t { collected.append((i, t)) }
            }
            return collected.sorted { $0.0 < $1.0 }.map { $0.1 }
        }
    }

    /// Realized effective fee rate (percent) for USDT-margined contracts since
    /// `startMs`: total fees paid divided by total traded notional. Returns nil
    /// when no qualifying volume is found. Inverse (coin-margined) contracts are
    /// skipped because their fee/notional are denominated in the base coin.
    static func effectiveFeePct(orders: [HistoryOrder], since startMs: Double,
                                ctVals: [String: Double]) -> Double? {
        var feeSum = 0.0
        var notionalSum = 0.0
        for o in orders {
            guard let t = o.eventTimeMs, t >= startMs else { continue }
            guard let id = o.instId, id.contains("-USDT-"), let cv = ctVals[id] else { continue }
            let sz = Double(o.accFillSz ?? "") ?? 0
            let px = Double(o.avgPx ?? "") ?? 0
            let notional = sz * cv * px
            guard notional > 0 else { continue }
            feeSum += abs(o.feeValue)
            notionalSum += notional
        }
        return notionalSum > 0 ? feeSum / notionalSum * 100 : nil
    }

    /// Start-of-period timestamps (ms) in the GMT+8 (Asia/Shanghai, no DST) zone:
    /// today 00:00, this week's Monday 00:00, and this month's 1st 00:00.
    static func periodStartsMs() -> (today: Double, week: Double, month: Double) {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "Asia/Shanghai")!
        cal.firstWeekday = 2 // Monday
        let now = Date()
        let ms = { (d: Date) in d.timeIntervalSince1970 * 1000 }
        let today = cal.startOfDay(for: now)
        let week = cal.dateInterval(of: .weekOfYear, for: now)?.start ?? today
        let month = cal.date(from: cal.dateComponents([.year, .month], from: now)) ?? today
        return (ms(today), ms(week), ms(month))
    }

    var totalUpl: Double {
        positions.reduce(0) { $0 + (Double($1.upl ?? "") ?? 0) }
    }

    var isDemo: Bool { creds.demo }
}
