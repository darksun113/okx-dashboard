# Windows Port — Handoff Doc

> **Audience:** an AI coding agent (or developer) tasked with porting **OKXMonitor**
> from macOS (Swift / SwiftUI) to Windows. You are working on a Windows machine
> and likely cannot run the macOS reference build. This document is the full
> spec — read it end to end before writing code.

---

## 1. What this app is

A small, **always-on-top floating widget** that polls a user's **OKX exchange
contract account** (perpetual swaps) every few seconds and shows:

| Block | What |
|---|---|
| 账户权益 / 未实现盈亏 | Total equity (USD) + margin ratio + sum of position uPnL |
| 已实现盈亏 (平仓) | Realized PnL for **today / this week / this month** in GMT+8, split into 盈利 / 亏损 / 手续费 / 合计 columns. Also shows account maker/taker fee rate + month-to-date effective fee rate. |
| 持仓 | All open SWAP positions: instrument, side (long/short), leverage, size, entry/mark price, uPnL, uPnL%, liq price |
| 挂单 | Open (pending) orders — auto-rotating carousel, 2 rows per page |
| 关注 | Auto-derived watchlist (positions ∪ pending orders); single-row carousel showing price + 2h / 6h / 24h % change |

Window is borderless, semi-transparent (`.ultraThinMaterial` on macOS — see §5
for Windows equivalents), draggable from the header, **no Dock/taskbar entry**,
quitting via an `×` button in the header.

There is **no trading capability**. The app uses **read-only API keys** and
only ever makes `GET` requests.

---

## 2. The macOS reference implementation

Everything lives in `Sources/OKXMonitor/`:

| File | Purpose |
|---|---|
| `main.swift` | Entry point; creates an accessory app (no Dock icon). |
| `AppDelegate.swift` | Creates the always-on-top `NSPanel`, restores position. |
| `ContentView.swift` | The whole UI — summary, PnL grid, positions list, two carousels. |
| `SettingsView.swift` | Modal sheet for API key / secret / passphrase / demo / refresh interval / **API host**. |
| `Store.swift` | `ObservableObject` holding live state; owns the refresh `Timer`; bucketing/derivation logic. |
| `OKXClient.swift` | Signed REST client. **One method per endpoint.** All signing centralized here. |
| `Models.swift` | Decodable structs for every OKX response. |
| `Keychain.swift` | Stores/retrieves credentials from macOS Keychain. |
| `PlainField.swift` | Custom NSTextField wrapper that disables smart-quote substitution (so the OKX passphrase isn't corrupted). |

When in doubt, the **Swift source is the ground truth.** Port the structure
1-to-1; don't redesign the data flow.

---

## 3. Recommended Windows tech stack

**Use .NET 8 (LTS) + WPF + C#.** Reasons:

- **.NET 8** is current LTS, mature, huge ecosystem, single-file self-contained publish.
- **WPF** gives you borderless windows, `Topmost`, transparency, easy drag — all out of the box. Much simpler than WinUI 3 (which requires MSIX packaging headaches for a tiny tray app).
- C# `HttpClient` + `System.Security.Cryptography.HMACSHA256` cover everything OKXClient.swift needs.
- **Windows Credential Manager** is the natural analog to macOS Keychain (see §6).
- `Microsoft.Windows.System.Power`, `H.NotifyIcon.Wpf` etc. are mature.

**Project skeleton:**

```
OKXMonitor.Windows/
  OKXMonitor.csproj          <TargetFramework>net8.0-windows</TargetFramework>; <UseWPF>true</UseWPF>
  App.xaml / App.xaml.cs     Single-instance check; tray icon
  MainWindow.xaml / .cs      Borderless, Topmost, draggable
  Views/
    SummaryView.xaml         账户权益 / 未实现盈亏
    PnlSectionView.xaml      已实现盈亏 + 费率
    PositionsView.xaml
    OrdersCarouselView.xaml
    WatchlistCarouselView.xaml
    SettingsWindow.xaml      Modal settings dialog
  Services/
    OkxClient.cs             Signed REST client (port of OKXClient.swift)
    CredentialStore.cs       Wraps Windows Credential Manager
    Store.cs                 Mirrors Store.swift; INotifyPropertyChanged + Timer
  Models/
    OkxResponses.cs          Records mirroring Models.swift
  Utils/
    PeriodCalculator.cs      GMT+8 today/week/month boundaries
```

**Alternatives considered & rejected for this scope:**
- WinUI 3 → packaging (MSIX), accessibility, single-EXE all harder
- Avalonia / Uno → cross-platform overhead not needed
- Electron / Tauri → 100MB+ runtime for a 300-pixel widget
- WinForms → no good binding story, painful styling

If the user explicitly wants a **single-file portable EXE**, publish with:
```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
~70MB EXE, runs on any Windows 10/11 x64 with no .NET install.

---

## 4. Feature checklist (in build order)

Build in this order so each step is independently testable:

1. **Skeleton window**: borderless, `Topmost`, draggable, ~340px wide, no taskbar (`ShowInTaskbar="False"`), `×` to quit. Don't connect to OKX yet — render mock data.
2. **Credential settings dialog** (apiKey / secretKey / passphrase / demo / host / interval). Save to Windows Credential Manager (§6). Validate non-empty.
3. **`OkxClient.GetBalance()` (the smallest signed call)** — wire it up; surface errors clearly. Confirm signing works against real OKX. **Do this before building any other endpoint.**
4. **`Store` + `DispatcherTimer` polling** → render summary block.
5. **Positions + pending orders** — port the data flow, render the lists.
6. **Pending-orders carousel** (2 rows per page, auto-flip every 3s).
7. **Historical orders** (`orders-history` + `orders-history-archive`, deduped) → realized PnL stats (today/week/month, GMT+8) → 5-column grid.
8. **Trade-fee rate** (`/api/v5/account/trade-fee`) + effective monthly fee rate (needs `/api/v5/public/instruments` for `ctVal`).
9. **Watchlist carousel** (derived from positions ∪ pending orders) — 1H candles, single-row carousel with auto-flip.
10. **Settings polish**: host quick-pick menu, refresh-interval slider, persist window position.

---

## 5. Always-on-top floating window (Windows specifics)

WPF gives you nearly everything declaratively:

```xml
<Window
    WindowStyle="None"
    AllowsTransparency="True"
    Background="#CC1E1E1E"          <!-- ~80% opaque dark; mimic the macOS material -->
    Topmost="True"
    ShowInTaskbar="False"
    ResizeMode="NoResize"
    Width="340"
    SizeToContent="Height"
    Title="OKX Monitor">
```

**Make it draggable from the header:**
```csharp
private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    if (e.ButtonState == MouseButtonState.Pressed) DragMove();
}
```

**Stay above full-screen apps too:** WPF `Topmost="True"` works above most full-screen
windows but NOT above exclusive-fullscreen (e.g., games). If you need that, set the
window's extended style `WS_EX_TOOLWINDOW | WS_EX_TOPMOST` via P/Invoke (uncommon).

**Hide from Alt-Tab:** set `WS_EX_TOOLWINDOW`:
```csharp
var hwnd = new WindowInteropHelper(this).Handle;
const int GWL_EXSTYLE = -20;
const int WS_EX_TOOLWINDOW = 0x80;
SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
```

**Restore window position across launches:** save `Left`/`Top` to user settings on `Closing`, restore on load. Clamp to current screen bounds in case monitors changed.

**Acrylic / Mica blur** (macOS `.ultraThinMaterial` equivalent on Windows 11): use the `Microsoft.UI.Composition` interop, or the simpler community library `WPF-UI` (`<ui:FluentWindow />`). Optional — a flat semi-transparent background is fine.

---

## 6. **SECURITY — read this twice**

The user's API key gives **read access to their trading account**. Treat it accordingly.

### 6.1 Credential storage

**Use Windows Credential Manager.** Do NOT write credentials to a file, registry plaintext, or `appsettings.json`.

Two implementations work:

**Option A — NuGet `Meziantou.Framework.Win32.CredentialManager` (recommended, simpler):**
```csharp
using Meziantou.Framework.Win32;

// Save
CredentialManager.WriteCredential(
    applicationName: "OKXMonitor",
    userName: "default",
    secret: JsonSerializer.Serialize(new { apiKey, secretKey, passphrase, demo }),
    persistence: CredentialPersistence.LocalMachine);

// Load
var cred = CredentialManager.ReadCredential("OKXMonitor");
```

The blob is encrypted by Windows with the user's login session key (DPAPI under
the hood) and stored in the per-user Credential Vault.

**Option B — P/Invoke wincred.h** if you can't add NuGets:
```csharp
[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
static extern bool CredWrite([In] ref CREDENTIAL credential, [In] uint flags);
```
Find a reference implementation under "C# CredWrite CredRead example" — keep it confined to `CredentialStore.cs`.

### 6.2 What to store

Mirror the Swift `Credentials` struct exactly:
```csharp
public sealed record Credentials(string ApiKey, string SecretKey, string Passphrase, bool Demo);
```

Serialize as JSON into a single Credential Manager entry. **Don't** create three separate
entries — the values are useless individually and an attacker who reads one will read all.

### 6.3 OKX API key permissions (tell the user explicitly in the UI)

The Settings dialog must show this text verbatim (translate as appropriate):
> 请使用仅勾选「读取」权限的 API key。本程序无法下单或提现。
>
> Use a READ-ONLY API key. This app cannot place orders or withdraw funds.

The OKX API key creation page lets users tick `Read` / `Trade` / `Withdraw`.
**Only `Read` is required for every endpoint this app calls.** If the user creates
a key with `Trade`/`Withdraw` and it leaks, that's a financial disaster.

### 6.4 Things that must never happen

- ❌ Credentials written to a `.json`, `.env`, `.txt`, `.log`, registry, or temp file.
- ❌ Credentials sent to telemetry, crash reports, error popups (sanitize messages).
- ❌ Credentials in `Console.WriteLine` / `Debug.WriteLine` / event tracing.
- ❌ Credentials passed on the command line (visible to other processes via WMI).
- ❌ Credentials in window titles (visible to `EnumWindows`).
- ❌ HTTP (un-encrypted) requests. Always `https://`. `HttpClient` validates certs by default — **do not** set `ServerCertificateCustomValidationCallback = ... => true`.
- ❌ Disabling certificate pinning / revocation checks.
- ❌ Using `Process.Start("cmd.exe", $"... {apiKey} ...")` — `Process.Start` arguments are inspectable.

### 6.5 Defensive coding

- Wrap credential reads in `try`/`catch`; show "credentials missing — open Settings" rather than crashing.
- Zero out secret strings when not needed: `Array.Clear(secretBytes, 0, secretBytes.Length)`. C# strings are immutable so use `byte[]` for the HMAC key path.
- Set `HttpClient.Timeout = TimeSpan.FromSeconds(15)` (same as Swift). Long timeouts mean queued requests hold credentials in memory.
- Add a "delete credentials" button in Settings that calls `CredentialManager.DeleteCredential("OKXMonitor")`.

### 6.6 Distribution

If you ship the EXE:
- **Sign it** with an Authenticode certificate (otherwise SmartScreen will scare users for weeks until enough downloads accumulate). For unsigned distribution, document the SmartScreen "More info → Run anyway" step.
- **Don't embed your own API keys** in the binary even for testing. The macOS reference's `test-creds.sh` reads interactively for a reason.
- Publish source on GitHub but **release binaries** via Releases (not in-repo) so they go through the signing pipeline.

---

## 7. OKX REST integration

Full docs: https://www.okx.com/docs-v5/en/

### 7.1 Signing (HMAC-SHA256, base64-encoded)

Every private request needs four headers:
```
OK-ACCESS-KEY:        <apiKey>
OK-ACCESS-SIGN:       Base64( HMAC-SHA256( secretKey,  timestamp + method + requestPath + body ) )
OK-ACCESS-TIMESTAMP:  ISO-8601 in UTC with milliseconds, e.g. 2026-06-06T03:14:15.123Z
OK-ACCESS-PASSPHRASE: <passphrase>
```

For demo accounts also add:
```
x-simulated-trading: 1
```

C# signing reference:
```csharp
static string Sign(string secret, string timestamp, string method, string path, string body = "") {
    var prehash = timestamp + method + path + body;
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(prehash));
    return Convert.ToBase64String(mac);
}

static string TimestampUtc() =>
    DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
```

**`requestPath` includes the query string** for GETs — e.g.
`/api/v5/market/candles?instId=BTC-USDT-SWAP&bar=1H&limit=25`. Match what's in the URL exactly or signing will fail with `OKX 50113`.

### 7.2 Response envelope

Every endpoint returns:
```json
{ "code": "0", "msg": "", "data": [ ... ] }
```
`code == "0"` means success. Anything else, surface as `"OKX {code}: {msg}"`. Some auth errors return without a `data` field — decode tolerantly.

### 7.3 Endpoints used

| Endpoint | Method | Purpose | Notes |
|---|---|---|---|
| `/api/v5/account/balance` | GET | Total equity, margin ratio | Smallest signed call; use this first to validate setup. |
| `/api/v5/account/positions?instType=SWAP` | GET | Open positions | Filter `pos != 0` client-side. |
| `/api/v5/trade/orders-pending` | GET | Live open orders | |
| `/api/v5/trade/orders-history?instType=SWAP&begin={ms}&limit=100` | GET | 7-day order history, **real-time** | Pass `begin` no older than 7 days! Older `begin` returns empty. Paginate via `after=<ordId>`. |
| `/api/v5/trade/orders-history-archive?instType=SWAP&begin={ms}&limit=100` | GET | 3-month order history, **may lag by hours for very recent fills** | Paginate via `after=<ordId>`. |
| `/api/v5/account/trade-fee?instType=SWAP` | GET | Account maker/taker fee tier | Cache — rarely changes. |
| `/api/v5/public/instruments?instType=SWAP` | GET | Contract specs (need `ctVal`) | Public, but signing it is harmless. Cache — rarely changes. |
| `/api/v5/market/candles?instId={x}&bar=1H&limit=25` | GET | 1-hour candles | Returns array-of-arrays — see §7.4. |

### 7.4 Candle decoding gotcha

`/api/v5/market/candles` returns each candle as a **JSON array of strings**, not an object:
```json
"data": [ ["1640000000000","43000","43100","42950","43050","100","100","5000","1"], … ]
```
Order: `[ts, open, high, low, close, vol, volCcy, volCcyQuote, confirm]`. You only need `close` (index 4). In C# parse with a custom converter:
```csharp
public sealed class Candle { public double Close { get; init; } }

public sealed class CandleConverter : JsonConverter<Candle> {
    public override Candle Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) {
        r.Read(); // [
        for (int i = 0; i < 4; i++) { r.GetString(); r.Read(); }
        var close = double.Parse(r.GetString()!, CultureInfo.InvariantCulture);
        while (r.TokenType != JsonTokenType.EndArray) r.Read();
        return new Candle { Close = close };
    }
    public override void Write(Utf8JsonWriter w, Candle v, JsonSerializerOptions o) => throw new NotSupportedException();
}
```
Candles come **newest-first**. For the watchlist: index 0 = now, index 2 ≈ 2h ago close, index 6 ≈ 6h ago, index 24 ≈ 24h ago.

### 7.5 Time-window math (GMT+8)

Realized PnL buckets use **Asia/Shanghai (UTC+8, no DST)** boundaries:
```csharp
var tz = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"); // Windows ID
var nowCn = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
var todayStart = TimeZoneInfo.ConvertTimeToUtc(nowCn.Date, tz);
// Week starts Monday:
var dow = ((int)nowCn.DayOfWeek + 6) % 7; // 0=Mon
var weekStart = TimeZoneInfo.ConvertTimeToUtc(nowCn.Date.AddDays(-dow), tz);
var monthStart = TimeZoneInfo.ConvertTimeToUtc(new DateTime(nowCn.Year, nowCn.Month, 1), tz);
```
Convert each `DateTime` to Unix-ms when comparing against order `fillTime`/`uTime`/`cTime` (OKX timestamps are ms strings).

### 7.6 Realized PnL math

For each historical order, OKX's `pnl` is **gross** (excludes fees) and `fee` is the trading fee (negative when charged). The 5-column PnL table:

| Column | Computation |
|---|---|
| 盈利 | Σ `pnl` for orders where `pnl > 0` |
| 亏损 | Σ `pnl` for orders where `pnl < 0` (negative) |
| 手续费 | Σ `fee` over **all** orders in the window (open + close), negative when charged |
| 合计 | 盈利 + 亏损 + 手续费 |

This counts **both opening and closing legs' fees** — that's intentional. Funding fees are NOT included (they don't appear on the order; they're in `positions-history.realizedPnl` if you ever need true net).

### 7.7 Effective fee rate (month-to-date)

```
effectivePct = Σ |fee|  /  Σ (accFillSz × ctVal × avgPx)  × 100
```
Restrict to USDT-margined instruments (`instId.Contains("-USDT-")`); coin-margined contracts mix currencies and would skew the number.

---

## 8. Host fallback (network resilience)

OKX has multiple domains for different regions. **Hard-coding `www.okx.com` is wrong** — it's blocked from many networks. The macOS app made this configurable for that reason.

**Required:**
- Default host: `www.okx.cab` (more reliable in CN).
- Setting field in UI lets user pick from `{www.okx.cab, www.okx.com, aws.okx.com}` or type a custom one.
- Persist via user settings, not Credential Manager.

**Optional polish:** on first launch, parallel-ping all known hosts (`/api/v5/public/time`, no auth) and pick the fastest. Show which host is in use in Settings.

---

## 9. Build, run, package

```powershell
# Dev loop
dotnet run --project OKXMonitor.Windows

# Production single-file EXE
dotnet publish OKXMonitor.Windows -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# Output: bin\Release\net8.0-windows\win-x64\publish\OKXMonitor.exe
```

For an installer, use **MSIX** (modern, signed, sandbox-friendly) or **Inno Setup** (classic, simple, single .exe installer). MSIX requires a cert; Inno doesn't but doesn't provide auto-updates.

**Auto-start at login:** add an entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (don't use HKLM — per-user only). Provide a Settings toggle.

---

## 10. Gotchas learned from the macOS build

1. **Passphrase smart-quote bug**. macOS's `NSTextField` auto-replaces straight quotes with curly quotes; an OKX passphrase containing a quote got silently mangled. WPF `TextBox` doesn't do this by default — but make sure you don't enable any auto-correct extensions, and **trim whitespace** (not the passphrase itself; passphrases can contain spaces, but leading/trailing whitespace from copy-paste is almost always wrong — match the Swift code's `.trimmingCharacters(in: .whitespacesAndNewlines)` for apiKey/secretKey but **not** for passphrase).

2. **Don't pass `begin` older than 7 days to `/orders-history`** (the 7-day endpoint). It returns empty rather than clamping. Always `Math.Max(beginMs, nowMs - 7 * 86400000)` when calling that endpoint. The archive endpoint accepts up to 3 months but **lags** for orders filled in the last ~hour. The fix is to call both and dedupe by `ordId`, with the real-time endpoint winning on duplicates.

3. **Watchlist concurrency**: the watchlist fans out one `/market/candles` call per instrument. With 10 instruments that's 10 parallel calls. OKX public rate limit is 40 req / 2s per IP — fine, but if the user has 30+ positions you'll need bounded concurrency (e.g., `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 6`).

4. **Cache reference data**: `/api/v5/account/trade-fee` and `/api/v5/public/instruments` change rarely — fetch once per session (lazy on first need), not every refresh. The macOS code does this in `Store.swift` via nil-check guards.

5. **OKX `fee` sign convention**: negative = charged, positive = rebate. Use `Math.Abs` when displaying "total fees paid" but use the raw value for net PnL.

6. **Network errors vs API errors are different**. A real network failure (host unreachable, TLS handshake fail) surfaces as `HttpRequestException`; a working connection that gets a non-2xx with an OKX body surfaces with `code != "0"`. Distinguish them in error messages — include the endpoint path so debugging is possible. See the macOS `OKXError` enum (`http`/`api`/`transport`) for the model.

7. **Decimal precision**: OKX returns all numeric fields as **strings** to preserve precision. Parse with `decimal.Parse(s, CultureInfo.InvariantCulture)` for money math, `double` is fine for display %. **Don't** use the system's default culture — that breaks on locales that use `,` as decimal separator.

8. **Single instance**: if the user launches the app twice, you'll get two floating widgets and double the API load. Use a named `Mutex` at startup; if already taken, focus the existing window and exit.

---

## 11. Acceptance checklist

Port is complete when, on a fresh Windows machine, the agent can demonstrate:

- [ ] First launch shows "configure credentials" prompt
- [ ] Settings dialog accepts API key/secret/passphrase/demo/host/interval; saved values persist across restarts
- [ ] Credentials live in **Windows Credential Manager** (verifiable via `Manage Windows Credentials` UI), NOT in any file under the install dir, `AppData`, or registry
- [ ] Window is borderless, semi-transparent, always-on-top, draggable, **not in taskbar**, no Alt-Tab entry
- [ ] All five blocks render with live data: equity/uPnL, realized PnL grid (5 columns), positions, pending-orders carousel, watchlist carousel
- [ ] PnL "今日" updates within seconds of a closed trade today (proving the orders-history-vs-archive dedup works)
- [ ] Fee row shows official Maker/Taker + month effective rate
- [ ] Switching API host in Settings and saving immediately reflects in subsequent requests
- [ ] Killing internet for 10s shows an inline error mentioning the failed endpoint path, then recovers when network returns
- [ ] Single-file EXE published with `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` runs on a clean Windows 10/11 box with no .NET install
- [ ] No credential strings appear in `Console`, `Debug`, event logs, or crash reports under any code path you can find

---

## 12. Useful refs

- OKX v5 API: https://www.okx.com/docs-v5/en/
- Windows Credential Manager: https://learn.microsoft.com/en-us/windows/win32/secauthn/credentials-management
- WPF custom windows: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/app-development/how-to-create-a-window-with-non-rectangular-borders
- Single-file deployment: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- Meziantou Credential Manager NuGet: https://github.com/meziantou/Meziantou.Framework/tree/main/src/Meziantou.Framework.Win32.CredentialManager

---

**One more reminder:** when in doubt about behavior, port from the Swift sources literally.
This doc describes the intent; the Swift code is the spec.
