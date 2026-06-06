import AppKit
import SwiftUI
import Combine

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var panel: NSPanel!
    private var hosting: NSHostingView<ContentView>!
    private let store = Store()
    private var statusItem: NSStatusItem!
    private var cancellables = Set<AnyCancellable>()

    func applicationDidFinishLaunching(_ notification: Notification) {
        let content = ContentView(store: store)
        let hosting = NSHostingView(rootView: content)
        self.hosting = hosting
        // Let SwiftUI draw all the way to the top edge (no title-bar safe-area gap).
        if #available(macOS 13.3, *) {
            hosting.safeAreaRegions = []
        }

        // A non-activating panel so it floats without stealing focus from other apps.
        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 340, height: 420),
            styleMask: [.titled, .closable, .nonactivatingPanel, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        panel.titleVisibility = .hidden
        panel.titlebarAppearsTransparent = true
        panel.standardWindowButton(.closeButton)?.isHidden = true
        panel.standardWindowButton(.miniaturizeButton)?.isHidden = true
        panel.standardWindowButton(.zoomButton)?.isHidden = true
        panel.isMovableByWindowBackground = true
        panel.isFloatingPanel = true
        panel.hidesOnDeactivate = false
        panel.level = .floating                          // always on top
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.contentView = hosting

        // Position near the top-right corner on first launch, then remember it.
        panel.setFrameAutosaveName("OKXMonitorPanel")
        ensureOnScreen(panel)

        panel.makeKeyAndOrderFront(nil)
        panel.orderFrontRegardless()
        self.panel = panel

        setupStatusItem()
        // Refresh the menu-bar title + window height whenever new data lands.
        store.$lastUpdated
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in
                self?.updateStatusTitle()
                DispatchQueue.main.async { self?.resizePanelToFit() }
            }
            .store(in: &cancellables)
        store.$errorMessage
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in self?.updateStatusTitle() }
            .store(in: &cancellables)
        store.$needsSetup
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in
                DispatchQueue.main.async { self?.resizePanelToFit() }
            }
            .store(in: &cancellables)

        store.start()
        updateStatusTitle()
        resizePanelToFit()
    }

    /// Size the window to exactly fit the SwiftUI content height, anchored at the
    /// top-left so it grows/shrinks downward instead of leaving empty space.
    private func resizePanelToFit() {
        guard let panel, let hosting else { return }
        hosting.layoutSubtreeIfNeeded()
        var size = hosting.fittingSize
        guard size.height > 1 else { return }
        size.width = 340
        if let v = panel.screen?.visibleFrame {
            size.height = min(size.height, v.height - 40)
        }
        let topLeft = NSPoint(x: panel.frame.minX, y: panel.frame.maxY)
        panel.setContentSize(size)
        panel.setFrameTopLeftPoint(topLeft)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    // MARK: - Menu bar status item

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.image = NSImage(
            systemSymbolName: "chart.line.uptrend.xyaxis",
            accessibilityDescription: "OKX"
        )
        statusItem.button?.imagePosition = .imageLeading
        let menu = NSMenu()
        menu.delegate = self
        statusItem.menu = menu
    }

    private func updateStatusTitle() {
        guard let button = statusItem.button else { return }
        if store.needsSetup {
            button.attributedTitle = NSAttributedString(string: " OKX")
            return
        }
        if store.lastUpdated == nil, store.errorMessage != nil {
            button.attributedTitle = NSAttributedString(
                string: " --",
                attributes: [.foregroundColor: NSColor.systemGray]
            )
            return
        }
        let pnl = store.totalUpl
        let color: NSColor = pnl > 0 ? .systemGreen : pnl < 0 ? .systemRed : .labelColor
        let sign = pnl > 0 ? "+" : ""
        let text = " \(sign)\(pnl.formatted(.number.precision(.fractionLength(2))))"
        button.attributedTitle = NSAttributedString(
            string: text,
            attributes: [
                .foregroundColor: color,
                .font: NSFont.monospacedDigitSystemFont(ofSize: 12, weight: .medium),
            ]
        )
    }

    // Rebuild the dropdown each time it opens so it reflects the latest data.
    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()

        if store.needsSetup {
            menu.addItem(disabledItem("未配置 API 凭据"))
            menu.addItem(.separator())
            addControlItems(to: menu)
            return
        }

        // Equity + total uPnL header.
        if let eq = store.balance?.totalEq, let v = Double(eq) {
            menu.addItem(disabledItem("账户权益  " + v.formatted(.number.precision(.fractionLength(2))) + " USD"))
        }
        let pnl = store.totalUpl
        menu.addItem(coloredItem("未实现盈亏  " + signed(pnl), value: pnl))

        menu.addItem(.separator())

        // Positions with their PnL.
        if store.positions.isEmpty {
            menu.addItem(disabledItem("无持仓"))
        } else {
            menu.addItem(sectionTitle("持仓"))
            for p in store.positions {
                let upl = Double(p.upl ?? "") ?? 0
                let ratio = (Double(p.uplRatio ?? "") ?? 0) * 100
                let name = (p.instId ?? "?").replacingOccurrences(of: "-SWAP", with: "")
                let side = sideLabel(p.posSide)
                let title = "\(name) \(side)   \(signed(upl))  (\(signedPct(ratio)))"
                menu.addItem(coloredItem(title, value: upl))
            }
        }

        // Open orders count.
        menu.addItem(.separator())
        menu.addItem(disabledItem("挂单  \(store.orders.count)"))

        if let err = store.errorMessage {
            menu.addItem(.separator())
            menu.addItem(coloredItem("⚠︎ " + err, value: -1))
        }

        menu.addItem(.separator())
        addControlItems(to: menu)
    }

    private func addControlItems(to menu: NSMenu) {
        let toggle = NSMenuItem(
            title: (panel.isVisible ? "隐藏窗口" : "显示窗口"),
            action: #selector(togglePanel), keyEquivalent: ""
        )
        toggle.target = self
        menu.addItem(toggle)

        let refresh = NSMenuItem(title: "立即刷新", action: #selector(refreshNow), keyEquivalent: "r")
        refresh.target = self
        menu.addItem(refresh)

        let settings = NSMenuItem(title: "设置…", action: #selector(showSettings), keyEquivalent: ",")
        settings.target = self
        menu.addItem(settings)

        menu.addItem(.separator())
        let quit = NSMenuItem(title: "退出 OKX Monitor", action: #selector(quit), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)
    }

    // MARK: - Actions

    @objc private func togglePanel() {
        if panel.isVisible {
            panel.orderOut(nil)
        } else {
            ensureOnScreen(panel)
            panel.makeKeyAndOrderFront(nil)
            panel.orderFrontRegardless()
        }
    }

    @objc private func refreshNow() {
        Task { await store.refresh() }
    }

    @objc private func showSettings() {
        ensureOnScreen(panel)
        panel.makeKeyAndOrderFront(nil)
        panel.orderFrontRegardless()
        NSApp.activate(ignoringOtherApps: true)
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }

    // MARK: - Menu item helpers

    private func disabledItem(_ title: String) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        item.isEnabled = false
        return item
    }

    private func sectionTitle(_ title: String) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        item.isEnabled = false
        item.attributedTitle = NSAttributedString(
            string: title,
            attributes: [.font: NSFont.systemFont(ofSize: 11, weight: .semibold),
                         .foregroundColor: NSColor.secondaryLabelColor]
        )
        return item
    }

    private func coloredItem(_ title: String, value: Double) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        item.isEnabled = false
        let color: NSColor = value > 0 ? .systemGreen : value < 0 ? .systemRed : .labelColor
        item.attributedTitle = NSAttributedString(
            string: title,
            attributes: [.foregroundColor: color,
                         .font: NSFont.monospacedDigitSystemFont(ofSize: 12, weight: .regular)]
        )
        return item
    }

    private func signed(_ v: Double) -> String {
        (v > 0 ? "+" : "") + v.formatted(.number.precision(.fractionLength(2)))
    }

    private func signedPct(_ v: Double) -> String {
        (v > 0 ? "+" : "") + v.formatted(.number.precision(.fractionLength(2))) + "%"
    }

    private func sideLabel(_ side: String?) -> String {
        switch (side ?? "").lowercased() {
        case "long": return "多"
        case "short": return "空"
        default: return ""
        }
    }

    /// If the (possibly restored) frame isn't visible on any screen, snap it to the
    /// main screen's top-right corner so the panel can never get "lost" off-screen.
    private func ensureOnScreen(_ window: NSWindow) {
        let frame = window.frame
        let visibleSomewhere = NSScreen.screens.contains { $0.visibleFrame.intersects(frame) }
        if !visibleSomewhere || frame.origin == .zero {
            let screen = NSScreen.main ?? NSScreen.screens.first
            if let v = screen?.visibleFrame {
                window.setFrameTopLeftPoint(NSPoint(x: v.maxX - 360, y: v.maxY - 20))
            }
        }
    }
}
