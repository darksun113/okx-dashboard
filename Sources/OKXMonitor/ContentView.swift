import SwiftUI

struct ContentView: View {
    @ObservedObject var store: Store
    @State private var showSettings = false

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider().opacity(0.5)
            if store.needsSetup {
                setupPrompt
            } else {
                VStack(alignment: .leading, spacing: 14) {
                    summary
                    pnlSection
                    positionsSection
                    ordersSection
                    watchlistSection
                }
                .padding(12)
            }
            Divider().opacity(0.5)
            footer
        }
        .frame(width: 340)
        .fixedSize(horizontal: false, vertical: true)
        .background(.ultraThinMaterial)
        .sheet(isPresented: $showSettings) {
            SettingsView(store: store)
        }
    }

    // MARK: - Header (draggable title bar)

    private var header: some View {
        HStack(spacing: 8) {
            Image(systemName: "chart.line.uptrend.xyaxis")
                .foregroundStyle(.green)
            Text("OKX Futures")
                .font(.system(size: 13, weight: .semibold))
            if store.isDemo {
                Text("DEMO").font(.system(size: 9, weight: .bold))
                    .padding(.horizontal, 4).padding(.vertical, 1)
                    .background(Color.orange.opacity(0.25)).clipShape(Capsule())
            }
            Spacer()
            if store.isLoading {
                ProgressView().controlSize(.small)
            }
            Button { Task { await store.refresh() } } label: {
                Image(systemName: "arrow.clockwise")
            }.buttonStyle(.plain).help("立即刷新")
            Button { showSettings = true } label: {
                Image(systemName: "gearshape")
            }.buttonStyle(.plain).help("设置")
            Button { NSApp.terminate(nil) } label: {
                Image(systemName: "xmark.circle.fill").foregroundStyle(.secondary)
            }.buttonStyle(.plain).help("退出")
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .contentShape(Rectangle())
        .background(WindowDragArea())
    }

    // MARK: - Summary (equity + total uPnL)

    private var summary: some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: 2) {
                Text("账户权益 (USD)").font(.caption2).foregroundStyle(.secondary)
                Text(fmtMoney(store.balance?.totalEq))
                    .font(.system(size: 20, weight: .bold, design: .rounded))
                if let mgn = store.balance?.mgnRatio, let v = Double(mgn) {
                    Text("保证金率 " + fmtPercent(v))
                        .font(.caption2).foregroundStyle(.secondary)
                }
            }
            Spacer()
            VStack(alignment: .trailing, spacing: 2) {
                Text("未实现盈亏").font(.caption2).foregroundStyle(.secondary)
                Text(fmtSignedMoney(store.totalUpl))
                    .font(.system(size: 20, weight: .bold, design: .rounded))
                    .foregroundStyle(pnlColor(store.totalUpl))
            }
        }
    }

    // MARK: - Realized PnL (closed contract orders, GMT+8 periods)

    private var pnlSection: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text("已实现盈亏 (平仓)").font(.system(size: 12, weight: .bold))
                Spacer()
            }
            Grid(alignment: .trailing, horizontalSpacing: 6, verticalSpacing: 4) {
                GridRow {
                    Text("").gridColumnAlignment(.leading)
                    Text("盈利").font(.system(size: 10)).foregroundStyle(.secondary)
                    Text("亏损").font(.system(size: 10)).foregroundStyle(.secondary)
                    Text("手续费").font(.system(size: 10)).foregroundStyle(.secondary)
                    Text("合计").font(.system(size: 10)).foregroundStyle(.secondary)
                }
                pnlRow("今日", store.pnlToday)
                pnlRow("本周", store.pnlWeek)
                pnlRow("本月", store.pnlMonth)
            }
            feeRateLine
        }
        .padding(8)
        .background(Color.primary.opacity(0.04))
        .clipShape(RoundedRectangle(cornerRadius: 6))
    }

    @ViewBuilder
    private var feeRateLine: some View {
        let parts: [String] = {
            var out: [String] = []
            if let mk = store.tradeFee?.makerPct { out.append("Maker " + fmtRate(mk)) }
            if let tk = store.tradeFee?.takerPct { out.append("Taker " + fmtRate(tk)) }
            if let eff = store.effectiveFeePct { out.append("本月实际 " + fmtRate(eff)) }
            return out
        }()
        if !parts.isEmpty {
            HStack(spacing: 4) {
                Image(systemName: "percent").font(.system(size: 8))
                Text(parts.joined(separator: " · "))
                    .font(.system(size: 9, design: .monospaced))
            }
            .foregroundStyle(.secondary)
            .padding(.top, 2)
        }
    }

    private func pnlRow(_ label: String, _ s: PnLStats) -> some View {
        GridRow {
            Text(label).font(.system(size: 11, weight: .medium))
                .gridColumnAlignment(.leading)
            Text(fmtSignedMoney(s.profit))
                .font(.system(size: 10, design: .monospaced))
                .foregroundStyle(s.profit > 0 ? .green : .secondary)
            Text(fmtSignedMoney(s.loss))
                .font(.system(size: 10, design: .monospaced))
                .foregroundStyle(s.loss < 0 ? .red : .secondary)
            Text(fmtSignedMoney(s.fee))
                .font(.system(size: 10, design: .monospaced))
                .foregroundStyle(s.fee < 0 ? .orange : .secondary)
            Text(fmtSignedMoney(s.net))
                .font(.system(size: 10, weight: .semibold, design: .monospaced))
                .foregroundStyle(pnlColor(s.net))
        }
    }

    // MARK: - Positions

    private var positionsSection: some View {
        VStack(alignment: .leading, spacing: 6) {
            sectionHeader("持仓", count: store.positions.count)
            if store.positions.isEmpty {
                emptyHint("无持仓")
            } else {
                ForEach(store.positions) { pos in
                    positionRow(pos)
                }
            }
        }
    }

    private func positionRow(_ pos: Position) -> some View {
        let upl = Double(pos.upl ?? "") ?? 0
        let ratio = Double(pos.uplRatio ?? "") ?? 0
        return VStack(alignment: .leading, spacing: 3) {
            HStack {
                Text(symbol(pos.instId)).font(.system(size: 12, weight: .semibold))
                sideTag(pos.posSide)
                if let lev = pos.lever, !lev.isEmpty {
                    Text("\(lev)x").font(.system(size: 10, weight: .medium))
                        .foregroundStyle(.secondary)
                }
                Spacer()
                Text(fmtSignedMoney(upl))
                    .font(.system(size: 12, weight: .semibold, design: .rounded))
                    .foregroundStyle(pnlColor(upl))
            }
            HStack(spacing: 10) {
                kv("数量", pos.pos ?? "-")
                kv("开仓", trimNum(pos.avgPx))
                kv("标记", trimNum(pos.markPx))
                Spacer()
                Text(fmtPercent(ratio * 100, withSign: true))
                    .font(.system(size: 10, design: .rounded))
                    .foregroundStyle(pnlColor(upl))
            }
            if let liq = pos.liqPx, let lv = Double(liq), lv > 0 {
                kv("强平价", trimNum(liq)).foregroundStyle(.orange)
            }
        }
        .padding(8)
        .background(Color.primary.opacity(0.04))
        .clipShape(RoundedRectangle(cornerRadius: 6))
    }

    // MARK: - Orders

    private var ordersSection: some View {
        VStack(alignment: .leading, spacing: 6) {
            sectionHeader("挂单", count: store.orders.count)
            if store.orders.isEmpty {
                emptyHint("无挂单")
            } else {
                OrdersCarousel(orders: store.orders)
            }
        }
    }

    // MARK: - Watchlist (auto-derived from positions + pending orders)

    private var watchlistSection: some View {
        VStack(alignment: .leading, spacing: 6) {
            sectionHeader("关注", count: store.watchlist.count)
            if store.watchlist.isEmpty {
                emptyHint("无持仓或挂单")
            } else {
                WatchlistCarousel(tokens: store.watchlist)
            }
        }
    }

    // MARK: - Footer

    private var footer: some View {
        HStack {
            if let err = store.errorMessage {
                Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.red)
                Text(err).lineLimit(1).truncationMode(.tail)
            } else if let t = store.lastUpdated {
                Image(systemName: "clock")
                Text("更新于 " + t.formatted(date: .omitted, time: .standard))
            } else {
                Text("等待数据…")
            }
            Spacer()
            Text("\(Int(store.refreshInterval))s")
        }
        .font(.system(size: 10))
        .foregroundStyle(.secondary)
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
    }

    private var setupPrompt: some View {
        VStack(spacing: 12) {
            Image(systemName: "key.fill").font(.largeTitle).foregroundStyle(.secondary)
            Text("请先配置只读 API 凭据").font(.callout)
            Button("打开设置") { showSettings = true }.buttonStyle(.borderedProminent)
        }
        .frame(maxWidth: .infinity)
        .padding(28)
    }

    // MARK: - Small helpers

    private func sectionHeader(_ title: String, count: Int) -> some View {
        HStack {
            Text(title).font(.system(size: 12, weight: .bold))
            Text("\(count)").font(.system(size: 10)).foregroundStyle(.secondary)
                .padding(.horizontal, 5).padding(.vertical, 1)
                .background(Color.primary.opacity(0.08)).clipShape(Capsule())
            Spacer()
        }
    }

    private func kv(_ k: String, _ v: String) -> some View {
        HStack(spacing: 3) {
            Text(k).font(.system(size: 10)).foregroundStyle(.secondary)
            Text(v).font(.system(size: 10, design: .monospaced))
        }
    }

    private func emptyHint(_ text: String) -> some View {
        Text(text).font(.caption).foregroundStyle(.secondary)
            .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func sideTag(_ side: String?) -> some View {
        let s = (side ?? "").lowercased()
        let isLong = s == "long" || s == "buy"
        let isShort = s == "short" || s == "sell"
        let label = s == "long" ? "多" : s == "short" ? "空" : s == "buy" ? "买" : s == "sell" ? "卖" : s
        let color: Color = isLong ? .green : isShort ? .red : .gray
        return Text(label.uppercased())
            .font(.system(size: 9, weight: .bold))
            .foregroundStyle(.white)
            .padding(.horizontal, 5).padding(.vertical, 1)
            .background(color.opacity(0.85)).clipShape(Capsule())
    }

    private func pnlColor(_ v: Double) -> Color {
        v > 0 ? .green : v < 0 ? .red : .secondary
    }
}

// MARK: - Open-orders carousel (2 rows, auto-rotating with a 3D flip)

struct OrdersCarousel: View {
    let orders: [PendingOrder]
    private let rowsPerPage = 2

    @State private var page = 0
    private let timer = Timer.publish(every: 3, on: .main, in: .common).autoconnect()

    private var pageCount: Int {
        max(1, (orders.count + rowsPerPage - 1) / rowsPerPage)
    }

    private var currentRows: [PendingOrder] {
        let start = (page % pageCount) * rowsPerPage
        let end = min(start + rowsPerPage, orders.count)
        guard start < end else { return [] }
        return Array(orders[start..<end])
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            ForEach(currentRows) { o in
                OrderRow(order: o)
            }
            // Keep height stable even when the last page has a single row.
            if currentRows.count < rowsPerPage {
                ForEach(0..<(rowsPerPage - currentRows.count), id: \.self) { _ in
                    Color.clear.frame(height: 18)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .id(page)
        .transition(.flipVertical)
        .clipped()
        .overlay(alignment: .bottomTrailing) { pageDots }
        .onReceive(timer) { _ in
            guard pageCount > 1 else { return }
            withAnimation(.easeInOut(duration: 0.55)) { page += 1 }
        }
    }

    private var pageDots: some View {
        HStack(spacing: 3) {
            if pageCount > 1 {
                ForEach(0..<pageCount, id: \.self) { i in
                    Circle()
                        .fill(i == page % pageCount ? Color.primary.opacity(0.7) : Color.primary.opacity(0.18))
                        .frame(width: 4, height: 4)
                }
            }
        }
        .padding(.trailing, 2)
    }
}

private struct OrderRow: View {
    let order: PendingOrder
    var body: some View {
        HStack {
            Text((order.instId ?? "—").replacingOccurrences(of: "-SWAP", with: ""))
                .font(.system(size: 11, weight: .medium))
            sideTag(order.posSide ?? order.side)
            Spacer()
            Text(trimNum(order.px)).font(.system(size: 11, design: .monospaced))
            Text("× \(order.sz ?? "-")").font(.system(size: 11, design: .monospaced))
                .foregroundStyle(.secondary)
        }
        .frame(height: 18)
    }
}

/// A split-flap style flip: the outgoing page rotates away around the X axis
/// while the incoming page flips in from the top.
extension AnyTransition {
    static var flipVertical: AnyTransition {
        .asymmetric(
            insertion: .modifier(active: FlipModifier(angle: -90, anchor: .top),
                                 identity: FlipModifier(angle: 0, anchor: .top)),
            removal: .modifier(active: FlipModifier(angle: 90, anchor: .bottom),
                               identity: FlipModifier(angle: 0, anchor: .bottom))
        )
    }
}

private struct FlipModifier: ViewModifier {
    let angle: Double
    let anchor: UnitPoint
    func body(content: Content) -> some View {
        content
            .rotation3DEffect(.degrees(angle), axis: (x: 1, y: 0, z: 0),
                              anchor: anchor, perspective: 0.6)
            .opacity(angle == 0 ? 1 : 0)
    }
}

// sideTag used by both position rows and order rows.
private func sideTag(_ side: String?) -> some View {
    let s = (side ?? "").lowercased()
    let isLong = s == "long" || s == "buy"
    let isShort = s == "short" || s == "sell"
    let label = s == "long" ? "多" : s == "short" ? "空" : s == "buy" ? "买" : s == "sell" ? "卖" : s
    let color: Color = isLong ? .green : isShort ? .red : .gray
    return Text(label.uppercased())
        .font(.system(size: 9, weight: .bold))
        .foregroundStyle(.white)
        .padding(.horizontal, 5).padding(.vertical, 1)
        .background(color.opacity(0.85)).clipShape(Capsule())
}

// MARK: - Watchlist carousel (1 row per page, auto-rotating)

struct WatchlistCarousel: View {
    let tokens: [WatchedToken]

    @State private var page = 0
    private let timer = Timer.publish(every: 3, on: .main, in: .common).autoconnect()

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            if let current = tokens.isEmpty ? nil : tokens[page % tokens.count] {
                WatchedTokenRow(token: current)
                    .id(page)
                    .transition(.flipVertical)
            } else {
                Color.clear.frame(height: 18)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .clipped()
        .overlay(alignment: .bottomTrailing) { pageDots }
        .onReceive(timer) { _ in
            guard tokens.count > 1 else { return }
            withAnimation(.easeInOut(duration: 0.55)) { page += 1 }
        }
    }

    private var pageDots: some View {
        HStack(spacing: 3) {
            if tokens.count > 1 {
                ForEach(0..<tokens.count, id: \.self) { i in
                    Circle()
                        .fill(i == page % tokens.count
                              ? Color.primary.opacity(0.7)
                              : Color.primary.opacity(0.18))
                        .frame(width: 4, height: 4)
                }
            }
        }
        .padding(.trailing, 2)
    }
}

private struct WatchedTokenRow: View {
    let token: WatchedToken
    var body: some View {
        HStack(spacing: 6) {
            Text(token.instId.replacingOccurrences(of: "-SWAP", with: ""))
                .font(.system(size: 11, weight: .semibold))
            Text(fmtNumber(token.price))
                .font(.system(size: 11, design: .monospaced))
                .foregroundStyle(.secondary)
            Spacer(minLength: 4)
            changeBadge("2h", token.change2h)
            changeBadge("6h", token.change6h)
            changeBadge("24h", token.change24h)
        }
        .frame(height: 18)
    }
}

private func changeBadge(_ label: String, _ v: Double?) -> some View {
    HStack(spacing: 2) {
        Text(label).font(.system(size: 9)).foregroundStyle(.secondary)
        Group {
            if let v {
                Text(fmtPercent(v, withSign: true))
                    .foregroundStyle(v > 0 ? .green : v < 0 ? .red : .secondary)
            } else {
                Text("—").foregroundStyle(.secondary)
            }
        }
        .font(.system(size: 10, design: .monospaced))
    }
}

// MARK: - Formatting

private func fmtNumber(_ v: Double) -> String {
    v.formatted(.number.precision(.fractionLength(0...6)))
}

private func fmtMoney(_ s: String?) -> String {
    guard let s, let v = Double(s) else { return "—" }
    return v.formatted(.number.precision(.fractionLength(2)))
}

private func fmtSignedMoney(_ v: Double) -> String {
    let sign = v > 0 ? "+" : ""
    return sign + v.formatted(.number.precision(.fractionLength(2)))
}

private func fmtRate(_ v: Double) -> String {
    v.formatted(.number.precision(.fractionLength(3))) + "%"
}

private func fmtPercent(_ v: Double, withSign: Bool = false) -> String {
    let sign = withSign && v > 0 ? "+" : ""
    return sign + v.formatted(.number.precision(.fractionLength(2))) + "%"
}

private func trimNum(_ s: String?) -> String {
    guard let s, let v = Double(s) else { return "—" }
    return v.formatted(.number.precision(.fractionLength(0...6)))
}

private func symbol(_ instId: String?) -> String {
    (instId ?? "—").replacingOccurrences(of: "-SWAP", with: "")
}

// Allows dragging the window by its header area.
struct WindowDragArea: NSViewRepresentable {
    func makeNSView(context: Context) -> NSView { DragView() }
    func updateNSView(_ nsView: NSView, context: Context) {}
    private final class DragView: NSView {
        override func mouseDown(with event: NSEvent) {
            window?.performDrag(with: event)
        }
    }
}
