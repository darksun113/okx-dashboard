import Foundation
import CryptoKit

enum OKXError: LocalizedError {
    case http(path: String, status: Int)
    case api(code: String, msg: String)
    case transport(path: String, underlying: Error)
    case missingCredentials

    var errorDescription: String? {
        switch self {
        case .http(let path, let c): return "HTTP \(c) — \(shortPath(path))"
        case .api(let code, let msg): return "OKX \(code): \(msg)"
        case .transport(let path, let err): return "\(err.localizedDescription) — \(shortPath(path))"
        case .missingCredentials: return "未配置 API 凭据"
        }
    }

    /// Drop the query string so the message stays compact in the footer.
    private func shortPath(_ p: String) -> String {
        p.split(separator: "?").first.map(String.init) ?? p
    }
}

/// Read-only OKX v5 REST client. Signs every request with HMAC-SHA256.
struct OKXClient {
    /// UserDefaults key holding the OKX REST host (without scheme).
    static let hostDefaultsKey = "okx_host"
    /// Default host. `.cab` is OKX's alternate domain for regions where the
    /// primary `.com` host is blocked or unreliable.
    static let defaultHost = "www.okx.cab"
    /// Hosts surfaced as quick picks in Settings.
    static let knownHosts = ["www.okx.cab", "www.okx.com", "aws.okx.com"]

    let creds: Credentials
    private var host: String {
        let saved = UserDefaults.standard.string(forKey: Self.hostDefaultsKey)?
            .trimmingCharacters(in: .whitespaces)
        return "https://" + (saved?.isEmpty == false ? saved! : Self.defaultHost)
    }

    private static let isoFormatter: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        f.timeZone = TimeZone(identifier: "UTC")
        return f
    }()

    private func sign(timestamp: String, method: String, path: String, body: String) -> String {
        let prehash = timestamp + method + path + body
        let key = SymmetricKey(data: Data(creds.secretKey.utf8))
        let mac = HMAC<SHA256>.authenticationCode(for: Data(prehash.utf8), using: key)
        return Data(mac).base64EncodedString()
    }

    private func request<T: Decodable>(path: String, as type: T.Type) async throws -> [T] {
        guard creds.isComplete else { throw OKXError.missingCredentials }
        let timestamp = Self.isoFormatter.string(from: Date())
        let method = "GET"
        let body = ""
        let signature = sign(timestamp: timestamp, method: method, path: path, body: body)

        var req = URLRequest(url: URL(string: host + path)!)
        req.httpMethod = method
        req.setValue(creds.apiKey, forHTTPHeaderField: "OK-ACCESS-KEY")
        req.setValue(signature, forHTTPHeaderField: "OK-ACCESS-SIGN")
        req.setValue(timestamp, forHTTPHeaderField: "OK-ACCESS-TIMESTAMP")
        req.setValue(creds.passphrase, forHTTPHeaderField: "OK-ACCESS-PASSPHRASE")
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        if creds.demo {
            req.setValue("1", forHTTPHeaderField: "x-simulated-trading")
        }
        req.timeoutInterval = 15

        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await URLSession.shared.data(for: req)
        } catch {
            throw OKXError.transport(path: path, underlying: error)
        }
        if let http = response as? HTTPURLResponse, !(200...299).contains(http.statusCode) {
            // OKX returns a JSON body with code/msg on auth errors (often without `data`); surface it.
            if let status = try? JSONDecoder().decode(OKXStatus.self, from: data), status.code != "0" {
                throw OKXError.api(code: status.code, msg: status.msg)
            }
            throw OKXError.http(path: path, status: http.statusCode)
        }
        let env = try JSONDecoder().decode(OKXResponse<T>.self, from: data)
        guard env.code == "0" else { throw OKXError.api(code: env.code, msg: env.msg) }
        return env.data ?? []
    }

    func balance() async throws -> AccountBalance? {
        try await request(path: "/api/v5/account/balance", as: AccountBalance.self).first
    }

    func positions() async throws -> [Position] {
        // SWAP + FUTURES contracts only.
        try await request(path: "/api/v5/account/positions?instType=SWAP", as: Position.self)
    }

    func pendingOrders() async throws -> [PendingOrder] {
        try await request(path: "/api/v5/trade/orders-pending", as: PendingOrder.self)
    }

    /// Recent 1-hour candles for an instrument. Public endpoint; we still send
    /// the signed headers, which OKX ignores on public routes.
    func candles1H(instId: String, limit: Int = 25) async throws -> [Candle] {
        let id = instId.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? instId
        return try await request(
            path: "/api/v5/market/candles?instId=\(id)&bar=1H&limit=\(limit)", as: Candle.self)
    }

    /// Current account trade-fee tier (maker/taker rates).
    func tradeFee(instType: String = "SWAP") async throws -> TradeFee? {
        try await request(path: "/api/v5/account/trade-fee?instType=\(instType)", as: TradeFee.self).first
    }

    /// Public contract specs (for contract values). No auth required, but the
    /// signed headers are harmlessly ignored by OKX on public endpoints.
    func instruments(instType: String = "SWAP") async throws -> [InstrumentSpec] {
        try await request(path: "/api/v5/public/instruments?instType=\(instType)", as: InstrumentSpec.self)
    }

    /// All completed SWAP/FUTURES orders since `beginMs`, deduped by ordId.
    ///
    /// Merges two sources: the 7-day `orders-history` (real-time, so the latest
    /// closes — and thus "今日"/"本周" — are never missed) and the 3-month
    /// `orders-history-archive` (covers the rest of month-to-date). When the
    /// requested window is within 7 days, the archive call is skipped.
    func ordersHistory(instType: String = "SWAP", beginMs: Double) async throws -> [HistoryOrder] {
        let sevenDaysAgoMs = (Date().timeIntervalSince1970 - 7 * 86_400) * 1000
        // The 7-day endpoint only retains a week of data; passing a `begin`
        // older than that makes it return nothing, which would drop today's
        // (real-time) orders. Clamp its window to the last 7 days.
        let recentBegin = max(beginMs, sevenDaysAgoMs)

        async let recent = paginatedOrders(
            endpoint: "orders-history", instType: instType, beginMs: recentBegin)
        async let archived: [HistoryOrder] = beginMs < sevenDaysAgoMs
            ? paginatedOrders(endpoint: "orders-history-archive", instType: instType, beginMs: beginMs)
            : []

        // Insert archived first, then recent, so the real-time copy wins for any
        // order returned by both endpoints.
        var byId: [String: HistoryOrder] = [:]
        for o in try await archived { if let id = o.ordId { byId[id] = o } }
        for o in try await recent { if let id = o.ordId { byId[id] = o } }
        return Array(byId.values)
    }

    /// Pages backward through an orders-history endpoint via the `after` (ordId)
    /// cursor, 100 per request, starting at `beginMs`.
    private func paginatedOrders(endpoint: String, instType: String, beginMs: Double) async throws -> [HistoryOrder] {
        let begin = String(Int64(beginMs))
        var all: [HistoryOrder] = []
        var after: String?
        // Safety cap: 30 pages × 100 = 3000 orders for the window.
        for _ in 0..<30 {
            var path = "/api/v5/trade/\(endpoint)?instType=\(instType)&limit=100&begin=\(begin)"
            if let after { path += "&after=\(after)" }
            let page = try await request(path: path, as: HistoryOrder.self)
            all.append(contentsOf: page)
            guard page.count == 100, let last = page.last?.ordId else { break }
            after = last
        }
        return all
    }
}
