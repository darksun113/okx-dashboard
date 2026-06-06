# OKXMonitor Windows — Dual-Layout Design

> **Date:** 2026-06-06
> **Status:** Approved (pending spec review)
> **Scope:** The Windows (.NET 8 / WPF / C#) port of OKXMonitor, with a Windows-specific
> requirement: two always-on-top layouts — a **portrait panel** (the macOS design, ported)
> and a **landscape 21:9 strip** — switchable within a single window.
>
> This spec layers on top of `docs/WINDOWS_PORT.md` (the original handoff doc). Where this
> spec is silent on REST/signing/security details, **`docs/WINDOWS_PORT.md` and the Swift
> sources in `Sources/OKXMonitor/` remain the ground truth.**

---

## 1. Goal

Port the macOS OKXMonitor widget to Windows, adding a second UI layout. The app is a
read-only, always-on-top desktop widget polling an OKX perpetual-swap account. It makes
**GET requests only** with read-only API keys — no trading capability.

Windows-specific addition: the user can switch the floating widget between a **portrait**
layout (the original stacked design) and a **landscape 2-row strip**, both always-on-top.

## 2. Decisions (locked)

| Dimension | Decision |
|---|---|
| Tech stack | .NET 8 (`net8.0-windows`) + WPF + C#. Build with installed SDK 9.0.x. |
| Layout coexistence | **One window, toggle between portrait/landscape.** Only one visible at a time. |
| Landscape form | **Draggable width, 2-row adaptive height.** Overflow auto-rotates (carousel). |
| Landscape realized-PnL | **Full 5-column grid** (盈利/亏损/手续费/合计 × 今日/本周/本月), same as portrait. |
| Window memory | **Per-layout**: portrait and landscape each persist their own Left/Top/Width/Height. |
| Default layout on launch | **Remember last used.** |
| Toggle triggers | **Header ⤢ button** + **tray right-click menu item.** No global hotkey. |
| Tray icon | **Dynamically render the uPnL number into the tray icon** (green/red), GDI-drawn. |
| Credential storage | **Windows Credential Manager**, single JSON blob (Meziantou NuGet). |
| Build approach | **Phased per handoff doc's 10 steps; checkpoint at key nodes.** Portrait first. |
| Signing validation | User enters a real **read-only** key in Settings and runs it themselves. Keys never touch the agent. |

## 3. Architecture

Approach chosen: **two `UserControl` views swapped inside one `MainWindow`** via a
`ContentControl`. The window owns all chrome/window-management; the views are pure
presentation bound to a single shared `Store`.

```
MainWindow                ── borderless · Topmost · draggable · no taskbar · Alt-Tab hidden · single instance
  ├─ Store (shared)       ── DispatcherTimer polling · INotifyPropertyChanged
  │     ├─ OkxClient        signed REST (all endpoints, orders-history dedup)
  │     ├─ CredentialStore  Windows Credential Manager (single JSON blob)
  │     ├─ AppSettings      host · interval · per-layout window geometry · last layout
  │     └─ PeriodCalculator GMT+8 today/week/month boundaries
  ├─ ContentControl       ── shows the active layout:
  │     ├─ PortraitView   (UserControl) — macOS 5-block stack, ~340 wide
  │     └─ LandscapeView  (UserControl) — 2-row wide strip, draggable width
  └─ TrayIcon             ── dynamic uPnL-number icon + right-click menu
```

**Key property:** both views bind the same `Store`. All data, signing, PnL bucketing,
and watchlist logic is layout-independent and written once.

### 3.1 Why this approach

- One window means one set of chrome/drag/topmost/single-instance code (vs duplicating
  across two `Window` classes).
- Swapping `UserControl`s keeps each layout's XAML isolated and independently testable
  (vs one monolithic view with tangled visibility conditions).

## 4. Shared layer (data + services)

This layer is **layout-agnostic** and is a faithful port of the Swift sources. Most of it
is already scaffolded under `OKXMonitor.Windows/`.

| Component | Ports from | Responsibility |
|---|---|---|
| `Models/OkxResponses.cs` | `Models.swift` | Decodable records; `CandleConverter` for array-of-strings candles; `PnLStats`, `WatchedToken` derivation. |
| `Services/OkxClient.cs` | `OKXClient.swift` | HMAC-SHA256 signing; one method per endpoint; `orders-history` + `-archive` dedup by `ordId`; paginated cursor. |
| `Services/OkxException.cs` | `OKXError` enum | Distinguishes Http / Api / Transport / MissingCredentials; messages never contain secrets. |
| `Services/Credentials.cs` + `CredentialStore.cs` | `Keychain.swift` | `record Credentials(ApiKey, SecretKey, Passphrase, Demo)`; single JSON blob in Credential Manager. |
| `Services/AppSettings.cs` | `UserDefaults` usage | Non-secret settings JSON in `%AppData%\OKXMonitor\settings.json`. |
| `Utils/PeriodCalculator.cs` | `Store.periodStartsMs` | GMT+8 (`China Standard Time`) today/week(Mon)/month start in Unix-ms. |
| `Services/Store.cs` | `Store.swift` | Owns `DispatcherTimer`; orchestrates refresh; derives PnL buckets, effective fee %, watchlist; caches trade-fee + ctVal. |

### 4.1 Signing (must match Swift exactly)

`OK-ACCESS-SIGN = Base64(HMAC-SHA256(secretKey, timestamp + "GET" + requestPath + ""))`,
`requestPath` **including** the query string. Timestamp `yyyy-MM-ddTHH:mm:ss.fffZ` UTC,
InvariantCulture. Demo accounts add header `x-simulated-trading: 1`. `HttpClient.Timeout =
15s`; certificate validation left at the secure default (never disabled).

### 4.2 Critical ported behaviors (from handoff §10 + Swift)

- **orders-history dedup:** clamp the 7-day endpoint's `begin` to `max(begin, now-7d)`;
  call the archive endpoint only when the window predates 7 days; real-time copy wins on
  duplicate `ordId`.
- **Realized PnL math:** 盈利 = Σ pnl>0; 亏损 = Σ pnl<0; 手续费 = Σ fee over all orders
  (open+close, negative when charged); 合计 = sum. Funding fees excluded.
- **Effective fee %:** Σ|fee| / Σ(accFillSz × ctVal × avgPx) × 100, USDT-margined only.
- **Candles:** array-of-strings, close = index 4, newest-first; index N ≈ N hours ago.
- **Decimal parsing:** always `InvariantCulture`; money via `decimal`, display % via `double`.
- **Reference-data caching:** `trade-fee` and `instruments` fetched once per session.
- **Watchlist concurrency:** one candles call per instId; cap parallelism (e.g. 6) for
  accounts with many positions.

## 5. Portrait view (`PortraitView`)

A 1:1 port of the macOS `ContentView`, top-to-bottom, ~340px wide, height fits content:

1. **Header** (drag region): icon · "OKX Futures" · DEMO badge · spinner · ⟳ refresh · ⚙ settings · ⤢ toggle · ✕ quit.
2. **Summary**: 账户权益 (USD) + 保证金率 | 未实现盈亏 (colored).
3. **Realized PnL**: 3×5 grid (今日/本周/本月 × 盈利/亏损/手续费/合计) + fee-rate line (Maker/Taker/本月实际).
4. **Positions**: per-position rows (symbol, side tag, leverage, uPnL, size/entry/mark, uPnL%, liq price).
5. **Pending orders**: carousel, 2 rows/page, auto-flip ~3s.
6. **Watchlist**: carousel, 1 row/page, price + 2h/6h/24h %.
7. **Footer**: last-updated / inline error (with endpoint path) / refresh interval.

## 6. Landscape view (`LandscapeView`)

Draggable width, two rows:

- **Row 1 — account glance (always full):** 📈 OKX + DEMO badge · 权益 · 未实现 uPnL ·
  保证金率 · **full 3×5 realized-PnL grid** (one dedicated segment) · fee-rate · right-aligned
  buttons (⟳ ⚙ ⤢ ✕). Row 1 is the drag region.
- **Row 2 — positions + pending orders + watchlist:** laid out horizontally. When the window
  is dragged too narrow to fit, content auto-rotates using the same flip animation/page-dots
  as the portrait carousels.

The full grid means row 1 needs adequate width; the user widens the window as needed. No
information is dropped relative to portrait — only re-arranged horizontally.

## 7. Window behavior

- `WindowStyle=None`, `AllowsTransparency=True`, semi-transparent dark background
  (≈ `#CC1E1E1E`), `Topmost=True`, `ResizeMode` allows width drag (landscape) /
  `SizeToContent=Height` (portrait), `ShowInTaskbar=False`.
- **Hide from Alt-Tab:** set `WS_EX_TOOLWINDOW` via P/Invoke in `OnSourceInitialized`.
- **Drag:** `DragMove()` on the header's `MouseLeftButtonDown`.
- **Single instance:** named `Mutex` at startup; if held, surface the existing window and exit.
- **Per-layout geometry persistence:** on move/resize/close, save the active layout's
  Left/Top/Width/Height to `AppSettings`; on toggle/launch, restore that layout's geometry,
  **clamped to a visible screen** (so it can never be lost off-screen if monitors changed).

### 7.1 Toggle flow

⤢ button (header, both layouts) and a tray menu item flip `AppSettings.LastLayout`, save the
outgoing layout's geometry, swap the `ContentControl` content, and restore the incoming
layout's geometry.

## 8. Tray icon

- A `NotifyIcon` whose icon bitmap is **redrawn each refresh** with the current total uPnL
  number (GDI/`System.Drawing`): green for ≥0, red for <0; a neutral glyph/`--` when not
  configured or on fetch failure (mirrors the macOS menu-bar behavior).
- Right-click menu: 显示/隐藏窗口 · 切换布局 · 立即刷新 · 设置… · 退出.

## 9. Settings dialog (`SettingsWindow`)

Modal. Fields: API Key / Secret Key / Passphrase / Demo toggle / API host (with quick-pick
`{www.okx.cab, www.okx.com, aws.okx.com}`) / refresh interval slider (2–60s). Must show the
read-only-key warning verbatim:

> 请使用仅勾选「读取」权限的 API key。本程序无法下单或提现。
> Use a READ-ONLY API key. This app cannot place orders or withdraw funds.

Trim whitespace on apiKey/secretKey but **not** the passphrase interior (only copy-paste
edge whitespace). Save credentials to Credential Manager; host/interval to `AppSettings`.
Include a **delete-credentials** button (`CredentialManager.DeleteCredential`).

## 10. Persistence schema

**Credential Manager** (`OKXMonitor` entry, single JSON blob):
`{ apiKey, secretKey, passphrase, demo }`.

**`%AppData%\OKXMonitor\settings.json`** (non-secret):
```json
{
  "host": "www.okx.cab",
  "refreshInterval": 5,
  "lastLayout": "portrait",
  "portrait":  { "left": 1560, "top": 40, "width": 340, "height": 520 },
  "landscape": { "left": 200, "top": 980, "width": 720, "height": 96 }
}
```

## 11. Security constraints (carried from handoff §6 — non-negotiable)

Credentials never written to any file/registry/argv/log/window-title/telemetry. Always HTTPS;
never disable cert validation. Sanitize all error messages. Use `byte[]` for the HMAC key
path where practical; wrap credential reads in try/catch with a "credentials missing — open
Settings" fallback. Distinguish transport vs API errors and include the endpoint path in
error messages.

## 12. Build sequencing (checkpoints)

Phased, portrait first (landscape reuses the same `Store`):

1. **Phase A — Portrait MVP & signing checkpoint (handoff steps 1–3 + minimal summary):**
   skeleton borderless/topmost/draggable/no-taskbar window with mock data → Settings +
   Credential Manager → `OkxClient.BalanceAsync()` wired → minimal `Store` (balance +
   positions) rendering the summary block. **CHECKPOINT:** user enters a real read-only key
   and confirms equity/uPnL render (signing works against real OKX).
2. **Phase B — Portrait full (steps 4–10):** full `Store` refresh; positions; pending-orders
   carousel; realized-PnL 3×5 grid (orders-history dedup); trade-fee + effective fee %;
   watchlist carousel; settings polish; per-layout geometry persistence (portrait only so far).
3. **Phase C — Landscape + toggle + tray:** `LandscapeView` (2-row, full grid, carousel);
   ⤢ toggle + tray menu item; per-layout geometry for landscape; dynamic-number tray icon.
4. **Phase D — Packaging:** single-file `dotnet publish -c Release -r win-x64 --self-contained
   -p:PublishSingleFile=true`; optional auto-start toggle (`HKCU\...\Run`).

## 13. Acceptance criteria

- Portrait and landscape both render all data live; switching via ⤢ / tray is instant and
  each layout returns to its own remembered position/size, clamped on-screen.
- First launch prompts for credentials; saved values persist across restarts; credentials
  live only in Credential Manager (verifiable via "Manage Windows Credentials").
- Window is borderless, semi-transparent, always-on-top, draggable, not in taskbar, not in Alt-Tab.
- Realized-PnL 今日 updates within seconds of a closed trade today (dedup works) in both layouts.
- Tray icon shows the live uPnL number (green/red).
- Killing the network shows an inline error naming the failed endpoint, then recovers.
- Single-file EXE runs on a clean Windows 10/11 x64 box with no .NET installed.
- No credential strings appear in any console/debug/log/crash path.

## 14. Out of scope (YAGNI for v1)

Global hotkey; acrylic/Mica blur (flat semi-transparent is fine); auto host-fallback ping;
funding-fee net PnL; spot accounts; equity sparkline; localization beyond existing CN/EN;
unit tests beyond signing/period/PnL if time permits.
