# OKXMonitor Windows Dual-Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Windows port of OKXMonitor — a read-only, always-on-top OKX perpetual-swap widget — with two switchable layouts (portrait panel + landscape 21:9 strip) in a single window.

**Architecture:** One borderless `MainWindow` owns all window chrome, a shared `Store` (polling + state), and a tray icon. A `ContentControl` swaps between two `UserControl` views (`PortraitView`, `LandscapeView`) bound to the same `Store`. All data/signing/PnL logic is layout-independent and lives in a pure, unit-tested service+model layer.

**Tech Stack:** .NET 8 (`net8.0-windows`), WPF, C#; `System.Text.Json`; `System.Security.Cryptography.HMACSHA256`; `Meziantou.Framework.Win32.CredentialManager`; `System.Drawing` (GDI tray-icon rendering); xUnit for tests.

**Reference sources (ground truth):** `Sources/OKXMonitor/*.swift`, `docs/WINDOWS_PORT.md`, and `docs/superpowers/specs/2026-06-06-windows-dual-layout-design.md`. When this plan is silent, those win.

---

## Pre-existing scaffolding

These files were already created during brainstorming and are referenced (and in some cases refactored) below. Treat them as a starting point; verify each against the code shown in its task:

- `OKXMonitor.Windows/OKXMonitor.Windows.csproj`
- `OKXMonitor.Windows/app.manifest`
- `OKXMonitor.Windows/Models/OkxResponses.cs`
- `OKXMonitor.Windows/Services/OkxClient.cs` (refactored in Task 7 to inject `HttpClient` + use `OkxSigner`)
- `OKXMonitor.Windows/Services/OkxException.cs`
- `OKXMonitor.Windows/Services/Credentials.cs`

## File structure (target)

```
OKXMonitor.sln
OKXMonitor.Windows/
  OKXMonitor.Windows.csproj      net8.0-windows · UseWPF · WinExe
  app.manifest                   PerMonitorV2 DPI
  App.xaml / App.xaml.cs         single-instance Mutex · creates Store, MainWindow, TrayIcon
  MainWindow.xaml / .cs          borderless · Topmost · no-taskbar · drag · WS_EX_TOOLWINDOW · hosts views · toggle
  Views/
    PortraitView.xaml / .cs      macOS 5-block stack
    LandscapeView.xaml / .cs     2-row wide strip
    SettingsWindow.xaml / .cs    modal settings
    Controls/
      Carousel.cs                auto-rotating pager (orders 2/page, watchlist 1/page)
  Services/
    OkxClient.cs                 signed REST (all endpoints, orders-history dedup)
    OkxSigner.cs                 pure HMAC-SHA256 signing + ISO timestamp
    OkxException.cs              Http / Api / Transport / MissingCredentials
    Credentials.cs               record(ApiKey,SecretKey,Passphrase,Demo)
    CredentialStore.cs           Windows Credential Manager single JSON blob
    AppSettings.cs               %AppData%\OKXMonitor\settings.json
    Store.cs                     DispatcherTimer polling + derivation (INotifyPropertyChanged)
  Models/
    OkxResponses.cs              records + CandleConverter + PnLStats + WatchedToken
  Utils/
    PeriodCalculator.cs          GMT+8 today/week/month (pure, takes utcNow)
    FeeCalculator.cs             effective fee % (pure)
    Format.cs                    money/number/percent formatting (InvariantCulture)
    Converters.cs                PnlColorConverter, SignedMoneyConverter (WPF IValueConverter)
  Interop/
    NativeMethods.cs             GetWindowLong/SetWindowLong, WS_EX_TOOLWINDOW
OKXMonitor.Tests/
  OKXMonitor.Tests.csproj        net8.0-windows · xUnit · references OKXMonitor.Windows
  FakeHttpMessageHandler.cs      canned responses keyed by request path
  OkxSignerTests.cs
  CandleConverterTests.cs
  PnLStatsTests.cs
  PeriodCalculatorTests.cs
  FeeCalculatorTests.cs
  WatchedTokenTests.cs
  OkxClientTests.cs              dedup + envelope + error mapping via fake handler
```

---

## Phase 0 — Solution, projects, NuGet

### Task 1: Create solution and wire up both projects

**Files:**
- Create: `OKXMonitor.sln`
- Create: `OKXMonitor.Tests/OKXMonitor.Tests.csproj`
- Modify: `OKXMonitor.Windows/OKXMonitor.Windows.csproj` (add `InternalsVisibleTo`)

- [ ] **Step 1: Create the solution and add the WPF project**

Run:
```powershell
cd C:\Users\mtion\claude\okx-dashboard
dotnet new sln -n OKXMonitor
dotnet sln add OKXMonitor.Windows/OKXMonitor.Windows.csproj
```
Expected: "Project ... added to the solution."

- [ ] **Step 2: Create the xUnit test project**

Run:
```powershell
dotnet new xunit -n OKXMonitor.Tests -o OKXMonitor.Tests
```
Then edit `OKXMonitor.Tests/OKXMonitor.Tests.csproj` so `<TargetFramework>` is `net8.0-windows` and it references the app:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\OKXMonitor.Windows\OKXMonitor.Windows.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add test project to the solution**

Run:
```powershell
dotnet sln add OKXMonitor.Tests/OKXMonitor.Tests.csproj
```

- [ ] **Step 4: Expose internals to the test project**

Add to `OKXMonitor.Windows/OKXMonitor.Windows.csproj` inside a new `<ItemGroup>`:
```xml
  <ItemGroup>
    <InternalsVisibleTo Include="OKXMonitor.Tests" />
  </ItemGroup>
```

- [ ] **Step 5: Restore and confirm both projects build**

Run:
```powershell
dotnet build OKXMonitor.sln
```
Expected: Build succeeded (the WPF project has no entry point yet but compiles; the test project compiles with the default `UnitTest1.cs`).

- [ ] **Step 6: Delete the template test and commit**

Delete `OKXMonitor.Tests/UnitTest1.cs`. Then:
```powershell
git add OKXMonitor.sln OKXMonitor.Tests OKXMonitor.Windows/OKXMonitor.Windows.csproj
git commit -m "chore: add solution and xUnit test project"
```

---

## Phase A — Shared layer (TDD)

### Task 2: Extract `OkxSigner` (pure signing) with a known HMAC vector

**Files:**
- Create: `OKXMonitor.Windows/Services/OkxSigner.cs`
- Test: `OKXMonitor.Tests/OkxSignerTests.cs`

- [ ] **Step 1: Write the failing tests**

`OKXMonitor.Tests/OkxSignerTests.cs`:
```csharp
using System;
using OKXMonitor.Services;
using Xunit;

public class OkxSignerTests
{
    // Well-known HMAC-SHA256 vector (Wikipedia): key="key",
    // msg="The quick brown fox jumps over the lazy dog"
    // => base64 97yD9DBThCSxMpjmqm+xQ+9NWaFJRhdZl0edvC0aPNg=
    [Fact]
    public void Sign_matches_known_vector()
    {
        var sig = OkxSigner.Sign(
            secret: "key",
            timestamp: "The quick brown fox jumps over the lazy dog",
            method: "", path: "", body: "");
        Assert.Equal("97yD9DBThCSxMpjmqm+xQ+9NWaFJRhdZl0edvC0aPNg=", sig);
    }

    [Fact]
    public void Sign_concatenates_in_okx_order()
    {
        // prehash must be timestamp + method + path + body, in that order.
        var a = OkxSigner.Sign("s", "T", "GET", "/p", "");
        var b = OkxSigner.Sign("s", "TGET/p", "", "", "");
        Assert.Equal(a, b);
    }

    [Fact]
    public void TimestampUtc_is_iso8601_millis_z()
    {
        var ts = OkxSigner.TimestampUtc(new DateTime(2026, 6, 6, 3, 14, 15, 123, DateTimeKind.Utc));
        Assert.Equal("2026-06-06T03:14:15.123Z", ts);
    }
}
```

- [ ] **Step 2: Run tests, verify they fail to compile**

Run: `dotnet test OKXMonitor.Tests --filter OkxSignerTests`
Expected: build error — `OkxSigner` does not exist.

- [ ] **Step 3: Implement `OkxSigner`**

`OKXMonitor.Windows/Services/OkxSigner.cs`:
```csharp
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OKXMonitor.Services;

/// Pure OKX request signing. Extracted from OKXClient.swift's `sign` so it can be
/// unit-tested in isolation. No I/O, no secrets retained.
public static class OkxSigner
{
    /// Base64( HMAC-SHA256( secret, timestamp + method + path + body ) ).
    public static string Sign(string secret, string timestamp, string method, string path, string body = "")
    {
        var prehash = timestamp + method + path + body;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(prehash));
        return Convert.ToBase64String(mac);
    }

    /// ISO-8601 UTC with milliseconds, e.g. 2026-06-06T03:14:15.123Z.
    public static string TimestampUtc(DateTime utcNow) =>
        utcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: Run tests, verify they pass**

Run: `dotnet test OKXMonitor.Tests --filter OkxSignerTests`
Expected: Passed! 3 tests.

- [ ] **Step 5: Commit**

```powershell
git add OKXMonitor.Windows/Services/OkxSigner.cs OKXMonitor.Tests/OkxSignerTests.cs
git commit -m "feat: pure OkxSigner with known-vector tests"
```

---

### Task 3: `CandleConverter` decoding test

**Files:**
- Test: `OKXMonitor.Tests/CandleConverterTests.cs`
- Verify: `OKXMonitor.Windows/Models/OkxResponses.cs` (`Candle`, `CandleConverter` already scaffolded)

- [ ] **Step 1: Write the failing test**

`OKXMonitor.Tests/CandleConverterTests.cs`:
```csharp
using System.Collections.Generic;
using System.Text.Json;
using OKXMonitor.Models;
using Xunit;

public class CandleConverterTests
{
    static JsonSerializerOptions Opts() => new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new CandleConverter() },
    };

    [Fact]
    public void Decodes_close_at_index_4()
    {
        // [ts, open, high, low, close, vol, volCcy, volCcyQuote, confirm]
        var json = """[["1640000000000","43000","43100","42950","43050","100","100","5000","1"]]""";
        var candles = JsonSerializer.Deserialize<List<Candle>>(json, Opts());
        Assert.Single(candles!);
        Assert.Equal(43050d, candles![0].Close);
    }

    [Fact]
    public void Envelope_with_candle_array_decodes()
    {
        var json = """
        {"code":"0","msg":"","data":[["1","1","2","0","9.5","1","1","1","1"]]}
        """;
        var env = JsonSerializer.Deserialize<OkxResponse<Candle>>(json, Opts());
        Assert.Equal("0", env!.Code);
        Assert.Equal(9.5d, env.Data![0].Close);
    }
}
```

- [ ] **Step 2: Run, verify pass (converter already exists)**

Run: `dotnet test OKXMonitor.Tests --filter CandleConverterTests`
Expected: Passed! 2 tests. If it fails, align `CandleConverter` with the scaffolded version in `Models/OkxResponses.cs`.

- [ ] **Step 3: Commit**

```powershell
git add OKXMonitor.Tests/CandleConverterTests.cs
git commit -m "test: candle array-of-strings decoding"
```

---

### Task 4: `PnLStats.From` bucketing test

**Files:**
- Test: `OKXMonitor.Tests/PnLStatsTests.cs`
- Verify: `OKXMonitor.Windows/Models/OkxResponses.cs` (`PnLStats`, `HistoryOrder`)

- [ ] **Step 1: Write the failing test**

`OKXMonitor.Tests/PnLStatsTests.cs`:
```csharp
using System.Collections.Generic;
using OKXMonitor.Models;
using Xunit;

public class PnLStatsTests
{
    static HistoryOrder Order(string pnl, string fee, string fillTimeMs) =>
        new() { Pnl = pnl, Fee = fee, FillTime = fillTimeMs };

    [Fact]
    public void Buckets_profit_loss_fee_and_net()
    {
        var orders = new List<HistoryOrder>
        {
            Order("100", "-0.5", "2000"),   // win, fee charged
            Order("-40", "-0.3", "2000"),   // loss, fee charged
            Order("0",   "-0.2", "2000"),   // open leg: no pnl, fee only
        };
        var s = PnLStats.From(orders, startMs: 1000);
        Assert.Equal(100d, s.Profit);
        Assert.Equal(-40d, s.Loss);
        Assert.Equal(-1.0d, s.Fee, 3);          // -0.5 -0.3 -0.2
        Assert.Equal(59.0d, s.Net, 3);          // 100 -40 -1.0
    }

    [Fact]
    public void Excludes_orders_before_window_start()
    {
        var orders = new List<HistoryOrder>
        {
            Order("100", "0", "500"),   // before start -> excluded
            Order("10",  "0", "1500"),  // included
        };
        var s = PnLStats.From(orders, startMs: 1000);
        Assert.Equal(10d, s.Profit);
        Assert.Equal(0d, s.Loss);
    }
}
```

- [ ] **Step 2: Run, verify pass**

Run: `dotnet test OKXMonitor.Tests --filter PnLStatsTests`
Expected: Passed! 2 tests.

- [ ] **Step 3: Commit**

```powershell
git add OKXMonitor.Tests/PnLStatsTests.cs
git commit -m "test: realized PnL bucketing (profit/loss/fee/net)"
```

---

### Task 5: `PeriodCalculator` (GMT+8) — testable with injected now

**Files:**
- Create: `OKXMonitor.Windows/Utils/PeriodCalculator.cs`
- Test: `OKXMonitor.Tests/PeriodCalculatorTests.cs`

- [ ] **Step 1: Write the failing test**

`OKXMonitor.Tests/PeriodCalculatorTests.cs`:
```csharp
using System;
using OKXMonitor.Utils;
using Xunit;

public class PeriodCalculatorTests
{
    static readonly TimeZoneInfo Cn = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    static DateTime CnOf(double ms) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime, Cn);

    [Fact]
    public void Today_start_is_cn_midnight_of_now()
    {
        var now = new DateTime(2026, 6, 6, 1, 0, 0, DateTimeKind.Utc); // 09:00 CN
        var (today, _, _) = PeriodCalculator.PeriodStartsMs(now);
        var t = CnOf(today);
        Assert.Equal(new DateTime(2026, 6, 6), t.Date);
        Assert.Equal(0, t.Hour);
        Assert.Equal(0, t.Minute);
    }

    [Fact]
    public void Week_start_is_a_cn_monday_not_after_now()
    {
        var now = new DateTime(2026, 6, 6, 1, 0, 0, DateTimeKind.Utc); // Sat 6 Jun, 09:00 CN
        var (_, week, _) = PeriodCalculator.PeriodStartsMs(now);
        var w = CnOf(week);
        Assert.Equal(DayOfWeek.Monday, w.DayOfWeek);
        Assert.Equal(new DateTime(2026, 6, 1), w.Date); // Mon 1 Jun
    }

    [Fact]
    public void Month_start_is_first_of_cn_month()
    {
        var now = new DateTime(2026, 6, 6, 1, 0, 0, DateTimeKind.Utc);
        var (_, _, month) = PeriodCalculator.PeriodStartsMs(now);
        var m = CnOf(month);
        Assert.Equal(1, m.Day);
        Assert.Equal(6, m.Month);
        Assert.Equal(0, m.Hour);
    }
}
```

- [ ] **Step 2: Run, verify it fails to compile**

Run: `dotnet test OKXMonitor.Tests --filter PeriodCalculatorTests`
Expected: build error — `PeriodCalculator` does not exist.

- [ ] **Step 3: Implement `PeriodCalculator`**

`OKXMonitor.Windows/Utils/PeriodCalculator.cs`:
```csharp
using System;

namespace OKXMonitor.Utils;

/// Start-of-period timestamps (Unix ms) in GMT+8 (Asia/Shanghai, no DST):
/// today 00:00, this week's Monday 00:00, this month's 1st 00:00.
/// Port of Store.swift's periodStartsMs; takes `utcNow` for deterministic tests.
public static class PeriodCalculator
{
    static readonly TimeZoneInfo Cn = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    public static (double today, double week, double month) PeriodStartsMs(DateTime utcNow)
    {
        var nowCn = TimeZoneInfo.ConvertTimeFromUtc(utcNow.ToUniversalTime(), Cn);

        var todayCn = nowCn.Date;                              // 00:00 CN today
        int dow = ((int)nowCn.DayOfWeek + 6) % 7;             // 0 = Monday
        var weekCn = todayCn.AddDays(-dow);                   // Monday 00:00 CN
        var monthCn = new DateTime(nowCn.Year, nowCn.Month, 1); // 1st 00:00 CN

        return (Ms(todayCn), Ms(weekCn), Ms(monthCn));
    }

    static double Ms(DateTime cnUnspecified)
    {
        var unspec = DateTime.SpecifyKind(cnUnspecified, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(unspec, Cn);
        return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    }
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test OKXMonitor.Tests --filter PeriodCalculatorTests`
Expected: Passed! 3 tests.

- [ ] **Step 5: Commit**

```powershell
git add OKXMonitor.Windows/Utils/PeriodCalculator.cs OKXMonitor.Tests/PeriodCalculatorTests.cs
git commit -m "feat: GMT+8 period boundaries with tests"
```

---

### Task 6: `FeeCalculator.EffectiveFeePct` (pure)

**Files:**
- Create: `OKXMonitor.Windows/Utils/FeeCalculator.cs`
- Test: `OKXMonitor.Tests/FeeCalculatorTests.cs`

- [ ] **Step 1: Write the failing test**

`OKXMonitor.Tests/FeeCalculatorTests.cs`:
```csharp
using System.Collections.Generic;
using OKXMonitor.Models;
using OKXMonitor.Utils;
using Xunit;

public class FeeCalculatorTests
{
    static HistoryOrder O(string inst, string fee, string sz, string px, string t) =>
        new() { InstId = inst, Fee = fee, AccFillSz = sz, AvgPx = px, FillTime = t };

    [Fact]
    public void Effective_pct_is_abs_fee_over_notional_for_usdt_only()
    {
        var orders = new List<HistoryOrder>
        {
            // USDT-margined: notional = sz*ctVal*px = 2*0.01*100 = 2.0; fee 0.01 abs
            O("BTC-USDT-SWAP", "-0.01", "2", "100", "2000"),
            // coin-margined: excluded entirely
            O("BTC-USD-SWAP",  "-9.99", "5", "100", "2000"),
        };
        var ctVals = new Dictionary<string, double> { ["BTC-USDT-SWAP"] = 0.01, ["BTC-USD-SWAP"] = 0.01 };
        var pct = FeeCalculator.EffectiveFeePct(orders, startMs: 1000, ctVals);
        Assert.NotNull(pct);
        Assert.Equal(0.01 / 2.0 * 100, pct!.Value, 6); // 0.5%
    }

    [Fact]
    public void Returns_null_when_no_qualifying_volume()
    {
        var pct = FeeCalculator.EffectiveFeePct(new List<HistoryOrder>(), 1000, new());
        Assert.Null(pct);
    }
}
```

- [ ] **Step 2: Run, verify fails to compile**

Run: `dotnet test OKXMonitor.Tests --filter FeeCalculatorTests`
Expected: build error — `FeeCalculator` missing.

- [ ] **Step 3: Implement `FeeCalculator`**

`OKXMonitor.Windows/Utils/FeeCalculator.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using OKXMonitor.Models;

namespace OKXMonitor.Utils;

/// Month-to-date effective fee rate (percent) for USDT-margined contracts:
/// Σ|fee| / Σ(accFillSz × ctVal × avgPx) × 100. Port of Store.effectiveFeePct.
public static class FeeCalculator
{
    public static double? EffectiveFeePct(IEnumerable<HistoryOrder> orders, double startMs,
                                          IReadOnlyDictionary<string, double> ctVals)
    {
        double feeSum = 0, notionalSum = 0;
        foreach (var o in orders)
        {
            if (o.EventTimeMs is not { } t || t < startMs) continue;
            if (o.InstId is not { } id || !id.Contains("-USDT-")) continue;
            if (!ctVals.TryGetValue(id, out var cv)) continue;
            double sz = Parse(o.AccFillSz), px = Parse(o.AvgPx);
            double notional = sz * cv * px;
            if (notional <= 0) continue;
            feeSum += Math.Abs(o.FeeValue);
            notionalSum += notional;
        }
        return notionalSum > 0 ? feeSum / notionalSum * 100 : null;
    }

    static double Parse(string? s) =>
        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test OKXMonitor.Tests --filter FeeCalculatorTests`
Expected: Passed! 2 tests.

- [ ] **Step 5: Commit**

```powershell
git add OKXMonitor.Windows/Utils/FeeCalculator.cs OKXMonitor.Tests/FeeCalculatorTests.cs
git commit -m "feat: effective fee % (USDT-margined) with tests"
```

---

### Task 7: Refactor `OkxClient` for testability + dedup tests

**Files:**
- Modify: `OKXMonitor.Windows/Services/OkxClient.cs`
- Create: `OKXMonitor.Tests/FakeHttpMessageHandler.cs`
- Test: `OKXMonitor.Tests/OkxClientTests.cs`

- [ ] **Step 1: Refactor `OkxClient` to inject an `HttpClient` and use `OkxSigner`**

In `OKXMonitor.Windows/Services/OkxClient.cs`: (a) delete the private `Sign` and `TimestampUtc` methods; (b) call `OkxSigner` instead; (c) add an injectable client. Replace the field + constructor + signing call sites:
```csharp
    static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    readonly Credentials _creds;
    readonly string _host;        // includes scheme, e.g. https://www.okx.cab
    readonly HttpClient _http;

    public OkxClient(Credentials creds, string? host, HttpClient? http = null)
    {
        _creds = creds;
        var h = string.IsNullOrWhiteSpace(host) ? DefaultHost : host.Trim();
        _host = "https://" + h;
        _http = http ?? SharedHttp;
    }
```
And inside `RequestAsync<T>`:
```csharp
        var timestamp = OkxSigner.TimestampUtc(DateTime.UtcNow);
        var signature = OkxSigner.Sign(_creds.SecretKey, timestamp, "GET", path, "");
        ...
        resp = await _http.SendAsync(req).ConfigureAwait(false);
```

- [ ] **Step 2: Create the fake handler**

`OKXMonitor.Tests/FakeHttpMessageHandler.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

/// Returns canned JSON keyed by a substring of the request path. Records calls.
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    readonly List<(string contains, string json)> _routes = new();
    public List<string> Requests { get; } = new();

    public FakeHttpMessageHandler On(string pathContains, string json)
    {
        _routes.Add((pathContains, json));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.PathAndQuery;
        Requests.Add(url);
        foreach (var (contains, json) in _routes)
            if (url.Contains(contains))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json),
                });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"code":"0","msg":"","data":[]}"""),
        });
    }
}
```

- [ ] **Step 3: Write the failing tests**

`OKXMonitor.Tests/OkxClientTests.cs`:
```csharp
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using OKXMonitor.Services;
using Xunit;

public class OkxClientTests
{
    static Credentials Creds() => new("k", "s", "p", false);

    [Fact]
    public async Task Balance_decodes_first_data_row()
    {
        var handler = new FakeHttpMessageHandler()
            .On("/api/v5/account/balance",
                """{"code":"0","msg":"","data":[{"totalEq":"12480.55","mgnRatio":"8.2"}]}""");
        var client = new OkxClient(Creds(), "www.okx.com", new HttpClient(handler));

        var bal = await client.BalanceAsync();

        Assert.Equal("12480.55", bal!.TotalEq);
    }

    [Fact]
    public async Task Api_error_code_surfaces_as_OkxException()
    {
        var handler = new FakeHttpMessageHandler()
            .On("/api/v5/account/balance", """{"code":"50113","msg":"Invalid sign","data":[]}""");
        var client = new OkxClient(Creds(), "www.okx.com", new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<OkxException>(() => client.BalanceAsync());
        Assert.Equal(OkxException.Kind.Api, ex.ErrorKind);
        Assert.Contains("50113", ex.Message);
    }

    [Fact]
    public async Task OrdersHistory_dedups_with_realtime_winning_over_archive()
    {
        // Same ordId "X" from both endpoints; recent (orders-history) must win.
        // Use a begin older than 7 days so BOTH endpoints are queried.
        var oldBeginMs = 0d;
        var handler = new FakeHttpMessageHandler()
            .On("orders-history-archive",
                """{"code":"0","msg":"","data":[{"ordId":"X","pnl":"1","fillTime":"100"},{"ordId":"Y","pnl":"2","fillTime":"100"}]}""")
            .On("orders-history",  // NOTE: also matches archive substring; ordering handled below
                """{"code":"0","msg":"","data":[{"ordId":"X","pnl":"999","fillTime":"100"}]}""");
        // Because "orders-history" is a substring of "orders-history-archive", register
        // the archive route FIRST (more specific) — the handler returns the first match.
        var client = new OkxClient(Creds(), "www.okx.com", new HttpClient(handler));

        var orders = await client.OrdersHistoryAsync(oldBeginMs);

        var x = orders.Single(o => o.OrdId == "X");
        Assert.Equal("999", x.Pnl);                  // realtime copy won
        Assert.Contains(orders, o => o.OrdId == "Y"); // archive-only survives
        Assert.Equal(2, orders.Count);
    }
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test OKXMonitor.Tests --filter OkxClientTests`
Expected: Passed! 3 tests. (If the dedup test's routing is flaky due to the substring overlap, the archive route is registered first and `FakeHttpMessageHandler` returns the first match, so `orders-history-archive` URLs hit the archive JSON and plain `orders-history` URLs fall through to the second route.)

- [ ] **Step 5: Run the whole suite + commit**

```powershell
dotnet test OKXMonitor.sln
git add OKXMonitor.Windows/Services/OkxClient.cs OKXMonitor.Tests/FakeHttpMessageHandler.cs OKXMonitor.Tests/OkxClientTests.cs
git commit -m "refactor: inject HttpClient into OkxClient; dedup + error-mapping tests"
```

---

### Task 8: `WatchedToken.From` test

**Files:**
- Test: `OKXMonitor.Tests/WatchedTokenTests.cs`
- Verify: `OKXMonitor.Windows/Models/OkxResponses.cs` (`WatchedToken`)

- [ ] **Step 1: Write the failing test**

`OKXMonitor.Tests/WatchedTokenTests.cs`:
```csharp
using System.Collections.Generic;
using OKXMonitor.Models;
using Xunit;

public class WatchedTokenTests
{
    static List<Candle> Series(params double[] closes)
    {
        var list = new List<Candle>();
        foreach (var c in closes) list.Add(new Candle { Close = c });
        return list; // newest-first, index 0 = now
    }

    [Fact]
    public void Computes_change_from_newest_first_series()
    {
        // index 0 = 110 (now), index 2 = 100 (2h ago) => +10%
        var candles = Series(110, 105, 100, 99, 98, 97, 90 /*idx6*/);
        var t = WatchedToken.From("SOL-USDT-SWAP", candles);
        Assert.NotNull(t);
        Assert.Equal(110d, t!.Price);
        Assert.Equal(10d, t.Change2h!.Value, 6);
        Assert.Equal((110 - 90) / 90.0 * 100, t.Change6h!.Value, 6);
        Assert.Null(t.Change24h); // series shorter than 25
    }

    [Fact]
    public void Returns_null_when_no_candles()
    {
        Assert.Null(WatchedToken.From("X", new List<Candle>()));
    }
}
```

- [ ] **Step 2: Run, verify pass**

Run: `dotnet test OKXMonitor.Tests --filter WatchedTokenTests`
Expected: Passed! 2 tests.

- [ ] **Step 3: Commit**

```powershell
git add OKXMonitor.Tests/WatchedTokenTests.cs
git commit -m "test: watchlist % change derivation"
```

---

### Task 9: `CredentialStore`, `AppSettings`, `Format`

**Files:**
- Create: `OKXMonitor.Windows/Services/CredentialStore.cs`
- Create: `OKXMonitor.Windows/Services/AppSettings.cs`
- Create: `OKXMonitor.Windows/Utils/Format.cs`

- [ ] **Step 1: Implement `CredentialStore`**

`OKXMonitor.Windows/Services/CredentialStore.cs`:
```csharp
using System;
using System.Text.Json;
using Meziantou.Framework.Win32;

namespace OKXMonitor.Services;

/// Stores ALL credentials as a single JSON blob in Windows Credential Manager
/// (DPAPI-backed). Mirrors Keychain.swift's single-item design. Never writes to
/// files/registry/argv. Reads are defensive — a missing entry yields Empty.
public static class CredentialStore
{
    const string AppName = "OKXMonitor";

    sealed record Blob(string ApiKey, string SecretKey, string Passphrase, bool Demo);

    public static void Save(Credentials c)
    {
        var json = JsonSerializer.Serialize(new Blob(c.ApiKey, c.SecretKey, c.Passphrase, c.Demo));
        CredentialManager.WriteCredential(
            applicationName: AppName,
            userName: "default",
            secret: json,
            persistence: CredentialPersistence.LocalMachine);
    }

    public static Credentials Load()
    {
        try
        {
            var cred = CredentialManager.ReadCredential(AppName);
            if (cred?.Password is { Length: > 0 } json)
            {
                var b = JsonSerializer.Deserialize<Blob>(json);
                if (b is not null)
                    return new Credentials(b.ApiKey, b.SecretKey, b.Passphrase, b.Demo);
            }
        }
        catch { /* fall through to Empty — UI shows "open Settings" */ }
        return Credentials.Empty;
    }

    public static void Delete()
    {
        try { CredentialManager.DeleteCredential(AppName); } catch { }
    }
}
```

- [ ] **Step 2: Implement `AppSettings`**

`OKXMonitor.Windows/Services/AppSettings.cs`:
```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OKXMonitor.Services;

public enum LayoutKind { Portrait, Landscape }

public sealed class WindowGeometry
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
}

/// Non-secret settings persisted to %AppData%\OKXMonitor\settings.json.
/// Credentials NEVER live here — see CredentialStore.
public sealed class AppSettings
{
    public string Host { get; set; } = OkxClient.DefaultHost;
    public double RefreshInterval { get; set; } = 5;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LayoutKind LastLayout { get; set; } = LayoutKind.Portrait;

    public WindowGeometry Portrait { get; set; } = new();
    public WindowGeometry Landscape { get; set; } = new();

    static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OKXMonitor");
    static string FilePath => Path.Combine(Dir, "settings.json");

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Opts) ?? new();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
        }
        catch { }
    }

    public WindowGeometry GeometryFor(LayoutKind k) => k == LayoutKind.Portrait ? Portrait : Landscape;
}
```

- [ ] **Step 3: Implement `Format`**

`OKXMonitor.Windows/Utils/Format.cs`:
```csharp
using System.Globalization;

namespace OKXMonitor.Utils;

/// Display formatting, always InvariantCulture (handoff §10.7).
public static class Format
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Money(string? s) =>
        double.TryParse(s, NumberStyles.Any, Inv, out var v) ? v.ToString("N2", Inv) : "—";

    public static string SignedMoney(double v) => (v > 0 ? "+" : "") + v.ToString("N2", Inv);

    public static string Rate(double v) => v.ToString("0.000", Inv) + "%";

    public static string Percent(double v, bool withSign = false) =>
        (withSign && v > 0 ? "+" : "") + v.ToString("0.00", Inv) + "%";

    public static string TrimNum(string? s) =>
        double.TryParse(s, NumberStyles.Any, Inv, out var v) ? v.ToString("0.######", Inv) : "—";

    public static string Number(double v) => v.ToString("0.######", Inv);

    public static string Symbol(string? instId) => (instId ?? "—").Replace("-SWAP", "");
}
```

- [ ] **Step 4: Build to verify (restores Meziantou NuGet)**

Run: `dotnet build OKXMonitor.Windows`
Expected: Build succeeded. If `Meziantou.Framework.Win32.CredentialManager` 1.4.5 fails to resolve, run `dotnet add OKXMonitor.Windows package Meziantou.Framework.Win32.CredentialManager` to pin the latest available version, then rebuild.

- [ ] **Step 5: Commit**

```powershell
git add OKXMonitor.Windows/Services/CredentialStore.cs OKXMonitor.Windows/Services/AppSettings.cs OKXMonitor.Windows/Utils/Format.cs
git commit -m "feat: credential store, app settings, formatting helpers"
```

---

## Phase A-UI — Skeleton window + Settings + minimal Store → SIGNING CHECKPOINT

### Task 10: Minimal `Store` (balance + positions)

**Files:**
- Create: `OKXMonitor.Windows/Services/Store.cs`

- [ ] **Step 1: Implement the minimal Store**

`OKXMonitor.Windows/Services/Store.cs`:
```csharp
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
        IsLoading = true; Raise(nameof(IsLoading));
        var client = new OkxClient(_creds, _settings.Host);
        try
        {
            var balTask = client.BalanceAsync();
            var posTask = client.PositionsAsync();
            await Task.WhenAll(balTask, posTask);
            Balance = balTask.Result;
            Positions = posTask.Result.Where(p => p.PosValue != 0).ToList();
            LastUpdated = DateTime.Now;
            ErrorMessage = null;
        }
        catch (Exception e)
        {
            ErrorMessage = e is OkxException ox ? ox.Message : e.Message;
        }
        finally
        {
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
                 nameof(IsLoading), nameof(ErrorMessage), nameof(LastUpdated), nameof(NeedsSetup) })
            Raise(n);
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OKXMonitor.Windows`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```powershell
git add OKXMonitor.Windows/Services/Store.cs
git commit -m "feat: minimal Store (balance + positions) with polling timer"
```

---

### Task 11: Interop + App single-instance + entry point

**Files:**
- Create: `OKXMonitor.Windows/Interop/NativeMethods.cs`
- Create: `OKXMonitor.Windows/App.xaml`
- Create: `OKXMonitor.Windows/App.xaml.cs`

- [ ] **Step 1: Implement native interop**

`OKXMonitor.Windows/Interop/NativeMethods.cs`:
```csharp
using System;
using System.Runtime.InteropServices;

namespace OKXMonitor.Interop;

/// Win32 calls to hide the window from Alt-Tab (WS_EX_TOOLWINDOW). Handoff §5.
public static class NativeMethods
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TOOLWINDOW = 0x80;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public static void HideFromAltTab(IntPtr hwnd)
    {
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);
    }
}
```

- [ ] **Step 2: Create `App.xaml` (no StartupUri — we construct manually)**

`OKXMonitor.Windows/App.xaml`:
```xml
<Application x:Class="OKXMonitor.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources />
</Application>
```

- [ ] **Step 3: Create `App.xaml.cs` with single-instance Mutex**

`OKXMonitor.Windows/App.xaml.cs`:
```csharp
using System;
using System.Threading;
using System.Windows;
using OKXMonitor.Services;

namespace OKXMonitor;

public partial class App : Application
{
    Mutex? _mutex;
    Store? _store;
    AppSettings? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(initiallyOwned: true, "OKXMonitor.SingleInstance", out bool isNew);
        if (!isNew)
        {
            // Another instance already runs; just exit (it owns the widget + API load).
            Shutdown();
            return;
        }

        _settings = AppSettings.Load();
        _store = new Store(_settings);

        var window = new MainWindow(_store, _settings);
        window.Show();
        _store.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 4: Build will fail (MainWindow missing) — that's expected; proceed to Task 12.**

- [ ] **Step 5: Commit**

```powershell
git add OKXMonitor.Windows/Interop/NativeMethods.cs OKXMonitor.Windows/App.xaml OKXMonitor.Windows/App.xaml.cs
git commit -m "feat: app entry point, single-instance mutex, alt-tab interop"
```

---

### Task 12: Skeleton `MainWindow` (chrome + summary, no toggle yet)

**Files:**
- Create: `OKXMonitor.Windows/MainWindow.xaml`
- Create: `OKXMonitor.Windows/MainWindow.xaml.cs`

This task renders the portrait summary inline so the signing checkpoint is reachable. Full portrait blocks and the toggle come in Phase B/C.

- [ ] **Step 1: Create `MainWindow.xaml`**

`OKXMonitor.Windows/MainWindow.xaml`:
```xml
<Window x:Class="OKXMonitor.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Background="#CC1E1E1E"
        Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize"
        Width="340" SizeToContent="Height" Title="OKX Monitor"
        TextOptions.TextFormattingMode="Display" UseLayoutRounding="True">
    <Border BorderBrush="#22FFFFFF" BorderThickness="1" CornerRadius="8">
        <StackPanel>
            <!-- Header (drag region) -->
            <Grid x:Name="Header" Background="#01000000" MouseLeftButtonDown="Header_MouseLeftButtonDown">
                <StackPanel Orientation="Horizontal" Margin="12,8">
                    <TextBlock Text="📈" Foreground="#34C759" FontSize="13"/>
                    <TextBlock Text="OKX Futures" Foreground="White" FontWeight="SemiBold"
                               FontSize="13" Margin="6,0,0,0"/>
                    <Border x:Name="DemoBadge" Background="#553A2A00" CornerRadius="3"
                            Padding="4,1" Margin="6,0,0,0" Visibility="Collapsed">
                        <TextBlock Text="DEMO" Foreground="#FFCF80" FontSize="9" FontWeight="Bold"/>
                    </Border>
                </StackPanel>
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,8,12,8">
                    <Button x:Name="RefreshBtn" Content="⟳" Click="Refresh_Click" Style="{StaticResource Chrome}"/>
                    <Button x:Name="SettingsBtn" Content="⚙" Click="Settings_Click" Style="{StaticResource Chrome}"/>
                    <Button x:Name="CloseBtn" Content="✕" Click="Close_Click" Style="{StaticResource Chrome}"/>
                </StackPanel>
            </Grid>

            <!-- Setup prompt -->
            <StackPanel x:Name="SetupPrompt" Margin="28" Visibility="Collapsed">
                <TextBlock Text="🔑" FontSize="28" HorizontalAlignment="Center" Foreground="#999"/>
                <TextBlock Text="请先配置只读 API 凭据" Foreground="#DDD"
                           HorizontalAlignment="Center" Margin="0,8"/>
                <Button Content="打开设置" Click="Settings_Click" HorizontalAlignment="Center" Padding="12,4"/>
            </StackPanel>

            <!-- Summary -->
            <Grid x:Name="SummaryBlock" Margin="12,10">
                <StackPanel HorizontalAlignment="Left">
                    <TextBlock Text="账户权益 (USD)" Foreground="#999" FontSize="11"/>
                    <TextBlock x:Name="EquityText" Text="—" Foreground="White" FontSize="20" FontWeight="Bold"/>
                    <TextBlock x:Name="MarginText" Foreground="#999" FontSize="11"/>
                </StackPanel>
                <StackPanel HorizontalAlignment="Right">
                    <TextBlock Text="未实现盈亏" Foreground="#999" FontSize="11" HorizontalAlignment="Right"/>
                    <TextBlock x:Name="UplText" Text="—" FontSize="20" FontWeight="Bold" HorizontalAlignment="Right"/>
                </StackPanel>
            </Grid>

            <!-- Footer -->
            <Border Background="#11FFFFFF" Padding="12,6">
                <Grid>
                    <TextBlock x:Name="StatusText" Foreground="#999" FontSize="10" Text="等待数据…"/>
                    <TextBlock x:Name="IntervalText" Foreground="#999" FontSize="10" HorizontalAlignment="Right"/>
                </Grid>
            </Border>
        </StackPanel>
    </Border>
    <Window.Resources>
        <Style x:Key="Chrome" TargetType="Button">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Foreground" Value="#AAA"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="Margin" Value="4,0,0,0"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}" Padding="3,1">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
</Window>
```

- [ ] **Step 2: Create `MainWindow.xaml.cs`**

`OKXMonitor.Windows/MainWindow.xaml.cs`:
```csharp
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using OKXMonitor.Interop;
using OKXMonitor.Services;
using OKXMonitor.Utils;

namespace OKXMonitor;

public partial class MainWindow : Window
{
    readonly Store _store;
    readonly AppSettings _settings;

    static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59));
    static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
    static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));

    public MainWindow(Store store, AppSettings settings)
    {
        InitializeComponent();
        _store = store;
        _settings = settings;
        _store.PropertyChanged += (_, _) => Dispatcher.Invoke(Render);
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.HideFromAltTab(hwnd);
        RestoreGeometry();
        Render();
    }

    void RestoreGeometry()
    {
        var g = _settings.GeometryFor(LayoutKind.Portrait);
        if (g.Left is { } l && g.Top is { } t)
        {
            Left = l; Top = t;
            EnsureOnScreen();
        }
        else
        {
            // First launch: top-right corner.
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 20;
            Top = area.Top + 20;
        }
    }

    void EnsureOnScreen()
    {
        var area = SystemParameters.VirtualScreen;
        if (Left < area.Left || Left > area.Right - 50) Left = SystemParameters.WorkArea.Right - Width - 20;
        if (Top < area.Top || Top > area.Bottom - 50) Top = SystemParameters.WorkArea.Top + 20;
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        var g = _settings.Portrait;
        g.Left = Left; g.Top = Top; g.Width = Width; g.Height = Height;
        _settings.Save();
    }

    void Render()
    {
        DemoBadge.Visibility = _store.IsDemo ? Visibility.Visible : Visibility.Collapsed;
        SetupPrompt.Visibility = _store.NeedsSetup ? Visibility.Visible : Visibility.Collapsed;
        SummaryBlock.Visibility = _store.NeedsSetup ? Visibility.Collapsed : Visibility.Visible;

        EquityText.Text = Format.Money(_store.Balance?.TotalEq);
        MarginText.Text = double.TryParse(_store.Balance?.MgnRatio, out var mr)
            ? "保证金率 " + Format.Percent(mr) : "";

        var upl = _store.TotalUpl;
        UplText.Text = Format.SignedMoney(upl);
        UplText.Foreground = upl > 0 ? Green : upl < 0 ? Red : Gray;

        IntervalText.Text = $"{(int)_store.RefreshInterval}s";
        StatusText.Text = _store.ErrorMessage is { } err ? "⚠ " + err
            : _store.LastUpdated is { } t ? "更新于 " + t.ToString("HH:mm:ss")
            : "等待数据…";
        StatusText.Foreground = _store.ErrorMessage is null ? Gray : Red;
    }

    void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await _store.RefreshAsync();

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Views.SettingsWindow(_store, _settings) { Owner = this };
        dlg.ShowDialog();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
```

- [ ] **Step 3: Build will fail (SettingsWindow missing) — proceed to Task 13 before running.**

- [ ] **Step 4: Commit**

```powershell
git add OKXMonitor.Windows/MainWindow.xaml OKXMonitor.Windows/MainWindow.xaml.cs
git commit -m "feat: skeleton MainWindow (chrome, drag, summary, geometry persistence)"
```

---

### Task 13: `SettingsWindow`

**Files:**
- Create: `OKXMonitor.Windows/Views/SettingsWindow.xaml`
- Create: `OKXMonitor.Windows/Views/SettingsWindow.xaml.cs`

- [ ] **Step 1: Create `SettingsWindow.xaml`**

`OKXMonitor.Windows/Views/SettingsWindow.xaml`:
```xml
<Window x:Class="OKXMonitor.Views.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="OKX API 设置" Width="360" SizeToContent="Height"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize" Background="#1E1E1E">
    <StackPanel Margin="16" TextElement.Foreground="#DDD">
        <TextBlock Text="OKX API 设置" FontSize="15" FontWeight="Bold" Margin="0,0,0,8"/>
        <TextBlock TextWrapping="Wrap" Foreground="#FFCF80" FontSize="11" Margin="0,0,0,10"
                   Text="请使用仅勾选「读取」权限的 API key。本程序无法下单或提现。&#x0a;Use a READ-ONLY API key. This app cannot place orders or withdraw funds."/>

        <TextBlock Text="API Key" FontSize="11" Foreground="#999"/>
        <TextBox x:Name="ApiKeyBox" Margin="0,2,0,8" FontFamily="Consolas"/>
        <TextBlock Text="Secret Key" FontSize="11" Foreground="#999"/>
        <TextBox x:Name="SecretBox" Margin="0,2,0,8" FontFamily="Consolas"/>
        <TextBlock Text="Passphrase" FontSize="11" Foreground="#999"/>
        <TextBox x:Name="PassphraseBox" Margin="0,2,0,8" FontFamily="Consolas"/>

        <CheckBox x:Name="DemoCheck" Content="模拟盘 (Demo Trading)" Foreground="#DDD" Margin="0,4"/>

        <TextBlock Text="API 域名 (无法连接时切换)" FontSize="11" Foreground="#999" Margin="0,6,0,0"/>
        <DockPanel Margin="0,2,0,8">
            <ComboBox x:Name="HostPreset" DockPanel.Dock="Right" Width="30"
                      SelectionChanged="HostPreset_Changed">
                <ComboBoxItem Content="www.okx.cab"/>
                <ComboBoxItem Content="www.okx.com"/>
                <ComboBoxItem Content="aws.okx.com"/>
            </ComboBox>
            <TextBox x:Name="HostBox" FontFamily="Consolas"/>
        </DockPanel>

        <DockPanel Margin="0,4">
            <TextBlock Text="刷新间隔" FontSize="12" DockPanel.Dock="Left" VerticalAlignment="Center"/>
            <TextBlock x:Name="IntervalLabel" DockPanel.Dock="Right" Width="40"
                       TextAlignment="Right" VerticalAlignment="Center"/>
            <Slider x:Name="IntervalSlider" Minimum="2" Maximum="60" TickFrequency="1"
                    IsSnapToTickEnabled="True" Margin="8,0"
                    ValueChanged="Interval_Changed"/>
        </DockPanel>

        <DockPanel Margin="0,12,0,0">
            <Button Content="删除凭据" Click="Delete_Click" DockPanel.Dock="Left"
                    Foreground="#FF6B6B" Padding="8,4"/>
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button Content="取消" Click="Cancel_Click" Padding="12,4" Margin="0,0,6,0"/>
                <Button x:Name="SaveBtn" Content="保存" Click="Save_Click" Padding="12,4" IsDefault="True"/>
            </StackPanel>
        </DockPanel>
    </StackPanel>
</Window>
```

- [ ] **Step 2: Create `SettingsWindow.xaml.cs`**

`OKXMonitor.Windows/Views/SettingsWindow.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using OKXMonitor.Services;

namespace OKXMonitor.Views;

public partial class SettingsWindow : Window
{
    readonly Store _store;
    readonly AppSettings _settings;

    public SettingsWindow(Store store, AppSettings settings)
    {
        InitializeComponent();
        _store = store;
        _settings = settings;

        var c = CredentialStore.Load();
        ApiKeyBox.Text = c.ApiKey;
        SecretBox.Text = c.SecretKey;
        PassphraseBox.Text = c.Passphrase;
        DemoCheck.IsChecked = c.Demo;
        HostBox.Text = string.IsNullOrWhiteSpace(_settings.Host) ? OkxClient.DefaultHost : _settings.Host;
        IntervalSlider.Value = _settings.RefreshInterval;
    }

    void HostPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (HostPreset.SelectedItem is ComboBoxItem item && item.Content is string h)
            HostBox.Text = h;
    }

    void Interval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IntervalLabel != null) IntervalLabel.Text = $"{(int)e.NewValue}s";
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyBox.Text.Trim();
        var secret = SecretBox.Text.Trim();
        var passphrase = PassphraseBox.Text; // do NOT trim interior; passphrases may contain spaces
        if (apiKey.Length == 0 || secret.Length == 0 || passphrase.Length == 0)
        {
            MessageBox.Show("API Key / Secret / Passphrase 不能为空。", "OKX");
            return;
        }

        _settings.Host = HostBox.Text.Trim();
        _settings.RefreshInterval = IntervalSlider.Value;
        _settings.Save();
        _store.UpdateSettings(_settings);
        _store.UpdateCredentials(new Credentials(apiKey, secret, passphrase, DemoCheck.IsChecked == true));
        Close();
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("确定删除已保存的凭据？", "OKX", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            CredentialStore.Delete();
            _store.UpdateCredentials(Credentials.Empty);
            Close();
        }
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build OKXMonitor.sln`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```powershell
git add OKXMonitor.Windows/Views/SettingsWindow.xaml OKXMonitor.Windows/Views/SettingsWindow.xaml.cs
git commit -m "feat: settings dialog (credentials, host, interval, delete)"
```

---

### Task 14: 🚩 SIGNING CHECKPOINT — run and validate against real OKX

**Files:** none (manual validation by the user)

- [ ] **Step 1: Run the app**

Run: `dotnet run --project OKXMonitor.Windows`
Expected: a borderless, always-on-top widget appears top-right, NOT in the taskbar, showing "请先配置只读 API 凭据".

- [ ] **Step 2: Hand off to the user for credential entry**

Tell the user: open ⚙ Settings, paste a **read-only** API Key / Secret / Passphrase (toggle Demo if a simulated account), pick a host if `www.okx.cab` is unreachable, Save. The agent must never request or handle the actual key values.

- [ ] **Step 3: Confirm signing works**

Expected after save: within a few seconds the summary shows real **账户权益 (USD)**, **保证金率**, and a colored **未实现盈亏** equal to the sum of position uPnL. The footer shows "更新于 HH:mm:ss" with no error.

Failure triage:
- `OKX 50113` → signature/timestamp mismatch — re-verify `OkxSigner` prehash order and that `requestPath` includes the query string.
- `OKX 50111`/`50102` → bad key/timestamp; check Demo toggle and system clock.
- Transport error naming the path → host blocked; switch host in Settings.

- [ ] **Step 4: Verify credential storage location**

Open Windows **Credential Manager → Windows Credentials**; confirm a `OKXMonitor` generic credential exists and that **no** credential strings appear in `%AppData%\OKXMonitor\settings.json`.

- [ ] **Step 5: Commit a checkpoint note (optional)**

```powershell
git commit --allow-empty -m "checkpoint: balance signing validated against real OKX"
```

**Do not proceed to Phase B until the user confirms the summary renders live data.**

---

## Phase B — Portrait full

### Task 15: Extend `Store.RefreshAsync` to full data set

**Files:**
- Modify: `OKXMonitor.Windows/Services/Store.cs`

- [ ] **Step 1: Add the remaining published state + derivation**

Add these properties to `Store` (alongside the Phase A ones):
```csharp
    public System.Collections.Generic.List<PendingOrder> Orders { get; private set; } = new();
    public PnLStats PnlToday { get; private set; }
    public PnLStats PnlWeek { get; private set; }
    public PnLStats PnlMonth { get; private set; }
    public TradeFee? TradeFee { get; private set; }
    public double? EffectiveFeePct { get; private set; }
    public System.Collections.Generic.List<WatchedToken> Watchlist { get; private set; } = new();

    System.Collections.Generic.Dictionary<string, double> _ctValCache = new();
```

- [ ] **Step 2: Replace `RefreshAsync` body with the full refresh**

Replace the `try` block in `RefreshAsync` with:
```csharp
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
```

- [ ] **Step 3: Add bounded-concurrency watchlist fetch**

Add to `Store`:
```csharp
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
```
Add `using System.Threading.Tasks;` and `using System.Linq;` if not present, and extend `RaiseAll()` to include `nameof(Orders)`, `nameof(PnlToday)`, `nameof(PnlWeek)`, `nameof(PnlMonth)`, `nameof(TradeFee)`, `nameof(EffectiveFeePct)`, `nameof(Watchlist)`.

- [ ] **Step 4: Build + run the existing tests**

Run: `dotnet build OKXMonitor.sln; if ($?) { dotnet test OKXMonitor.sln }`
Expected: Build succeeded; all tests pass.

- [ ] **Step 5: Commit**

```powershell
git add OKXMonitor.Windows/Services/Store.cs
git commit -m "feat: full Store refresh (orders, PnL buckets, fees, watchlist)"
```

---

### Task 16: `Carousel` control

**Files:**
- Create: `OKXMonitor.Windows/Views/Controls/Carousel.cs`

- [ ] **Step 1: Implement an auto-rotating pager**

`OKXMonitor.Windows/Views/Controls/Carousel.cs`:
```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace OKXMonitor.Views.Controls;

/// A vertical pager that shows `RowsPerPage` items per page and auto-advances
/// every 3s, with page dots. Mirrors the macOS OrdersCarousel/WatchlistCarousel.
public sealed class Carousel : Control
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    StackPanel? _rows;
    StackPanel? _dots;
    int _page;

    public int RowsPerPage { get; set; } = 2;

    /// Builds a UI element for one item.
    public Func<object, UIElement>? RowFactory { get; set; }

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(Carousel),
            new PropertyMetadata(null, (d, _) => ((Carousel)d).Rebuild()));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public Carousel()
    {
        var outer = new StackPanel();
        _rows = new StackPanel();
        _dots = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 2, 0) };
        outer.Children.Add(_rows);
        outer.Children.Add(_dots);
        Template = new ControlTemplate(typeof(Carousel)) { VisualTree = Wrap(outer) };
        _timer.Tick += (_, _) => { _page++; Rebuild(); };
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    static FrameworkElementFactory Wrap(UIElement child)
    {
        var f = new FrameworkElementFactory(typeof(ContentControl));
        f.SetValue(ContentControl.ContentProperty, child);
        return f;
    }

    List<object> Items() => ItemsSource?.Cast<object>().ToList() ?? new();

    int PageCount(int count) => Math.Max(1, (count + RowsPerPage - 1) / RowsPerPage);

    void Rebuild()
    {
        if (_rows is null || _dots is null || RowFactory is null) return;
        _rows.Children.Clear();
        _dots.Children.Clear();

        var items = Items();
        int pages = PageCount(items.Count);
        int page = pages == 0 ? 0 : _page % pages;
        int start = page * RowsPerPage;

        for (int i = start; i < Math.Min(start + RowsPerPage, items.Count); i++)
            _rows.Children.Add(RowFactory(items[i]));

        if (pages > 1)
            for (int p = 0; p < pages; p++)
                _dots.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 4, Height = 4, Margin = new Thickness(1.5, 0, 1.5, 0),
                    Fill = System.Windows.Media.Brushes.Gray,
                    Opacity = p == page ? 0.8 : 0.25,
                });
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OKXMonitor.Windows`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```powershell
git add OKXMonitor.Windows/Views/Controls/Carousel.cs
git commit -m "feat: auto-rotating Carousel control"
```

---

### Task 17: `PortraitView` with all five blocks

**Files:**
- Create: `OKXMonitor.Windows/Views/PortraitView.xaml`
- Create: `OKXMonitor.Windows/Views/PortraitView.xaml.cs`
- Modify: `OKXMonitor.Windows/MainWindow.xaml` (host the view), `MainWindow.xaml.cs`

- [ ] **Step 1: Create `PortraitView.xaml`** (header is provided by MainWindow; this view is the body)

`OKXMonitor.Windows/Views/PortraitView.xaml`:
```xml
<UserControl x:Class="OKXMonitor.Views.PortraitView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ctl="clr-namespace:OKXMonitor.Views.Controls"
             TextElement.Foreground="#DDD">
    <StackPanel Margin="12,8">
        <!-- Summary -->
        <Grid Margin="0,2,0,12">
            <StackPanel HorizontalAlignment="Left">
                <TextBlock Text="账户权益 (USD)" Foreground="#999" FontSize="11"/>
                <TextBlock x:Name="EquityText" Foreground="White" FontSize="20" FontWeight="Bold"/>
                <TextBlock x:Name="MarginText" Foreground="#999" FontSize="11"/>
            </StackPanel>
            <StackPanel HorizontalAlignment="Right">
                <TextBlock Text="未实现盈亏" Foreground="#999" FontSize="11" HorizontalAlignment="Right"/>
                <TextBlock x:Name="UplText" FontSize="20" FontWeight="Bold" HorizontalAlignment="Right"/>
            </StackPanel>
        </Grid>

        <!-- Realized PnL 3x5 grid -->
        <Border Background="#0AFFFFFF" CornerRadius="6" Padding="8" Margin="0,0,0,12">
            <StackPanel>
                <TextBlock Text="已实现盈亏 (平仓)" FontWeight="Bold" FontSize="12" Margin="0,0,0,4"/>
                <Grid x:Name="PnlGrid">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="*"/><ColumnDefinition Width="*"/><ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions>
                        <RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/>
                    </Grid.RowDefinitions>
                    <!-- header row -->
                    <TextBlock Grid.Row="0" Grid.Column="1" Text="盈利" FontSize="10" Foreground="#999" TextAlignment="Right"/>
                    <TextBlock Grid.Row="0" Grid.Column="2" Text="亏损" FontSize="10" Foreground="#999" TextAlignment="Right"/>
                    <TextBlock Grid.Row="0" Grid.Column="3" Text="手续费" FontSize="10" Foreground="#999" TextAlignment="Right"/>
                    <TextBlock Grid.Row="0" Grid.Column="4" Text="合计" FontSize="10" Foreground="#999" TextAlignment="Right"/>
                </Grid>
                <TextBlock x:Name="FeeRateLine" FontSize="9" Foreground="#999" FontFamily="Consolas" Margin="0,4,0,0"/>
            </StackPanel>
        </Border>

        <!-- Positions -->
        <TextBlock Text="持仓" FontWeight="Bold" FontSize="12"/>
        <StackPanel x:Name="PositionsPanel" Margin="0,4,0,12"/>

        <!-- Pending orders carousel -->
        <TextBlock Text="挂单" FontWeight="Bold" FontSize="12"/>
        <ctl:Carousel x:Name="OrdersCarousel" RowsPerPage="2" Margin="0,4,0,12"/>

        <!-- Watchlist carousel -->
        <TextBlock Text="关注" FontWeight="Bold" FontSize="12"/>
        <ctl:Carousel x:Name="WatchCarousel" RowsPerPage="1" Margin="0,4,0,4"/>
    </StackPanel>
</UserControl>
```

- [ ] **Step 2: Create `PortraitView.xaml.cs`** (renders from a `Store`, builds rows in code)

`OKXMonitor.Windows/Views/PortraitView.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OKXMonitor.Models;
using OKXMonitor.Services;
using OKXMonitor.Utils;

namespace OKXMonitor.Views;

public partial class PortraitView : UserControl
{
    Store? _store;
    static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59));
    static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
    static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
    static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));

    public PortraitView() { InitializeComponent(); }

    public void Bind(Store store)
    {
        _store = store;
        OrdersCarousel.RowFactory = o => OrderRow((PendingOrder)o);
        WatchCarousel.RowFactory = t => WatchRow((WatchedToken)t);
        Render();
    }

    static Brush PnlColor(double v) => v > 0 ? Green : v < 0 ? Red : Gray;

    public void Render()
    {
        if (_store is null) return;
        EquityText.Text = Format.Money(_store.Balance?.TotalEq);
        MarginText.Text = double.TryParse(_store.Balance?.MgnRatio, out var mr) ? "保证金率 " + Format.Percent(mr) : "";
        UplText.Text = Format.SignedMoney(_store.TotalUpl);
        UplText.Foreground = PnlColor(_store.TotalUpl);

        BuildPnlRows();
        BuildFeeLine();
        BuildPositions();
        OrdersCarousel.ItemsSource = _store.Orders;
        WatchCarousel.ItemsSource = _store.Watchlist;
    }

    void BuildPnlRows()
    {
        // Remove any previously-added data rows (keep the header row at Row 0).
        for (int i = PnlGrid.Children.Count - 1; i >= 0; i--)
            if (Grid.GetRow(PnlGrid.Children[i]) > 0) PnlGrid.Children.RemoveAt(i);

        AddPnlRow(1, "今日", _store!.PnlToday);
        AddPnlRow(2, "本周", _store.PnlWeek);
        AddPnlRow(3, "本月", _store.PnlMonth);
    }

    void AddPnlRow(int row, string label, PnLStats s)
    {
        Add(row, 0, label, Gray, false);
        Add(row, 1, Format.SignedMoney(s.Profit), s.Profit > 0 ? Green : Gray, true);
        Add(row, 2, Format.SignedMoney(s.Loss), s.Loss < 0 ? Red : Gray, true);
        Add(row, 3, Format.SignedMoney(s.Fee), s.Fee < 0 ? Orange : Gray, true);
        Add(row, 4, Format.SignedMoney(s.Net), PnlColor(s.Net), true);
    }

    void Add(int row, int col, string text, Brush color, bool mono)
    {
        var tb = new TextBlock
        {
            Text = text, Foreground = color, FontSize = mono ? 10 : 11,
            TextAlignment = col == 0 ? TextAlignment.Left : TextAlignment.Right,
            FontFamily = mono ? new FontFamily("Consolas") : new FontFamily("Segoe UI"),
        };
        Grid.SetRow(tb, row); Grid.SetColumn(tb, col);
        PnlGrid.Children.Add(tb);
    }

    void BuildFeeLine()
    {
        var parts = new System.Collections.Generic.List<string>();
        if (_store!.TradeFee?.MakerPct is { } mk) parts.Add("Maker " + Format.Rate(mk));
        if (_store.TradeFee?.TakerPct is { } tk) parts.Add("Taker " + Format.Rate(tk));
        if (_store.EffectiveFeePct is { } eff) parts.Add("本月实际 " + Format.Rate(eff));
        FeeRateLine.Text = parts.Count > 0 ? string.Join(" · ", parts) : "";
    }

    void BuildPositions()
    {
        PositionsPanel.Children.Clear();
        if (_store!.Positions.Count == 0)
        {
            PositionsPanel.Children.Add(new TextBlock { Text = "无持仓", Foreground = Gray, FontSize = 11 });
            return;
        }
        foreach (var p in _store.Positions)
            PositionsPanel.Children.Add(PositionRow(p));
    }

    UIElement PositionRow(Position p)
    {
        double upl = p.UplValue;
        double ratio = double.TryParse(p.UplRatio, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 0;

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new TextBlock { Text = Format.Symbol(p.InstId), FontWeight = FontWeights.SemiBold, FontSize = 12 });
        left.Children.Add(SideTag(p.PosSide));
        if (!string.IsNullOrEmpty(p.Lever))
            left.Children.Add(new TextBlock { Text = $" {p.Lever}x", Foreground = Gray, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(left, Dock.Left);
        top.Children.Add(left);
        top.Children.Add(new TextBlock
        {
            Text = Format.SignedMoney(upl), Foreground = PnlColor(upl), FontSize = 12,
            FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right,
        });

        var detail = new TextBlock
        {
            FontSize = 10, Foreground = Gray, FontFamily = new FontFamily("Consolas"),
            Text = $"数量 {p.Pos ?? "-"}  开仓 {Format.TrimNum(p.AvgPx)}  标记 {Format.TrimNum(p.MarkPx)}  {Format.Percent(ratio * 100, true)}",
        };

        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        box.Children.Add(top);
        box.Children.Add(detail);
        if (double.TryParse(p.LiqPx, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var liq) && liq > 0)
            box.Children.Add(new TextBlock { Text = "强平价 " + Format.TrimNum(p.LiqPx), Foreground = Orange, FontSize = 10, FontFamily = new FontFamily("Consolas") });

        return new Border { Background = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 4), Child = box };
    }

    UIElement OrderRow(PendingOrder o)
    {
        var dp = new DockPanel { Height = 18 };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new TextBlock { Text = Format.Symbol(o.InstId), FontSize = 11, FontWeight = FontWeights.Medium });
        left.Children.Add(SideTag(o.PosSide ?? o.Side));
        DockPanel.SetDock(left, Dock.Left);
        dp.Children.Add(left);
        dp.Children.Add(new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Right, FontSize = 11, Foreground = Gray,
            FontFamily = new FontFamily("Consolas"), Text = $"{Format.TrimNum(o.Px)} × {o.Sz ?? "-"}",
        });
        return dp;
    }

    UIElement WatchRow(WatchedToken t)
    {
        var dp = new StackPanel { Orientation = Orientation.Horizontal, Height = 18 };
        dp.Children.Add(new TextBlock { Text = Format.Symbol(t.InstId), FontSize = 11, FontWeight = FontWeights.SemiBold });
        dp.Children.Add(new TextBlock { Text = " " + Format.Number(t.Price), FontSize = 11, Foreground = Gray, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(4, 0, 8, 0) });
        dp.Children.Add(ChangeBadge("2h", t.Change2h));
        dp.Children.Add(ChangeBadge("6h", t.Change6h));
        dp.Children.Add(ChangeBadge("24h", t.Change24h));
        return dp;
    }

    UIElement ChangeBadge(string label, double? v)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0) };
        sp.Children.Add(new TextBlock { Text = label + " ", FontSize = 9, Foreground = Gray });
        sp.Children.Add(new TextBlock
        {
            FontSize = 10, FontFamily = new FontFamily("Consolas"),
            Text = v is { } x ? Format.Percent(x, true) : "—",
            Foreground = v is { } y ? PnlColor(y) : Gray,
        });
        return sp;
    }

    UIElement SideTag(string? side)
    {
        var s = (side ?? "").ToLowerInvariant();
        bool isLong = s is "long" or "buy";
        bool isShort = s is "short" or "sell";
        string label = s switch { "long" => "多", "short" => "空", "buy" => "买", "sell" => "卖", _ => s };
        var color = isLong ? Green : isShort ? Red : Gray;
        return new Border
        {
            Background = color, CornerRadius = new CornerRadius(7), Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.Bold },
        };
    }
}
```

- [ ] **Step 3: Host `PortraitView` in `MainWindow`**

In `MainWindow.xaml`, replace the inline `SummaryBlock` Grid with a content host:
```xml
            <ContentControl x:Name="BodyHost"/>
```
In `MainWindow.xaml.cs`: remove the summary-specific lines from `Render()`; add a `PortraitView` field, instantiate it in the constructor, bind it, and put it in `BodyHost`:
```csharp
    readonly Views.PortraitView _portrait = new();
    // in constructor after InitializeComponent:
    BodyHost.Content = _portrait;
    // in Render(): toggle setup vs body, then:
    BodyHost.Visibility = _store.NeedsSetup ? Visibility.Collapsed : Visibility.Visible;
    if (!_store.NeedsSetup) { _portrait.Bind(_store); }
```
(Keep the header DemoBadge + footer rendering in `MainWindow.Render()`.)

- [ ] **Step 4: Build + run; manual verification**

Run: `dotnet build OKXMonitor.sln; if ($?) { dotnet run --project OKXMonitor.Windows }`
Expected: all five blocks render with live data — equity/uPnL, 3×5 realized-PnL grid + fee line, positions list, pending-orders carousel flipping every 3s, watchlist carousel.

- [ ] **Step 5: Commit**

```powershell
git add OKXMonitor.Windows/Views/PortraitView.xaml OKXMonitor.Windows/Views/PortraitView.xaml.cs OKXMonitor.Windows/MainWindow.xaml OKXMonitor.Windows/MainWindow.xaml.cs
git commit -m "feat: full portrait view (all five blocks)"
```

---

## Phase C — Landscape + toggle + tray

### Task 18: `LandscapeView` (2-row strip, full grid)

**Files:**
- Create: `OKXMonitor.Windows/Views/LandscapeView.xaml`
- Create: `OKXMonitor.Windows/Views/LandscapeView.xaml.cs`

- [ ] **Step 1: Create `LandscapeView.xaml`** (row 1 = account glance incl. full grid; row 2 = positions/orders/watch carousel)

`OKXMonitor.Windows/Views/LandscapeView.xaml`:
```xml
<UserControl x:Class="OKXMonitor.Views.LandscapeView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ctl="clr-namespace:OKXMonitor.Views.Controls"
             TextElement.Foreground="#DDD">
    <StackPanel Margin="10,6">
        <!-- ROW 1: account glance -->
        <StackPanel Orientation="Horizontal" x:Name="Row1">
            <StackPanel Margin="0,0,12,0">
                <TextBlock Text="账户权益" Foreground="#999" FontSize="9"/>
                <TextBlock x:Name="EquityText" Foreground="White" FontSize="14" FontWeight="Bold"/>
                <TextBlock x:Name="MarginText" Foreground="#999" FontSize="9"/>
            </StackPanel>
            <StackPanel Margin="0,0,12,0">
                <TextBlock Text="未实现盈亏" Foreground="#999" FontSize="9"/>
                <TextBlock x:Name="UplText" FontSize="14" FontWeight="Bold"/>
            </StackPanel>
            <Border BorderBrush="#22FFFFFF" BorderThickness="1,0,0,0" Padding="12,0,0,0">
                <Grid x:Name="PnlGrid">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto" MinWidth="46"/>
                        <ColumnDefinition Width="Auto" MinWidth="46"/><ColumnDefinition Width="Auto" MinWidth="46"/>
                        <ColumnDefinition Width="Auto" MinWidth="46"/>
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions><RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/></Grid.RowDefinitions>
                    <TextBlock Grid.Row="0" Grid.Column="1" Text="盈利" FontSize="9" Foreground="#999" TextAlignment="Right"/>
                    <TextBlock Grid.Row="0" Grid.Column="2" Text="亏损" FontSize="9" Foreground="#999" TextAlignment="Right"/>
                    <TextBlock Grid.Row="0" Grid.Column="3" Text="手续费" FontSize="9" Foreground="#999" TextAlignment="Right"/>
                    <TextBlock Grid.Row="0" Grid.Column="4" Text="合计" FontSize="9" Foreground="#999" TextAlignment="Right"/>
                </Grid>
            </Border>
            <TextBlock x:Name="FeeRateLine" Foreground="#999" FontSize="9" FontFamily="Consolas"
                       VerticalAlignment="Center" Margin="12,0,0,0"/>
        </StackPanel>

        <!-- ROW 2: positions + orders + watch -->
        <ctl:Carousel x:Name="Row2Carousel" RowsPerPage="1" Margin="0,6,0,0"/>
    </StackPanel>
</UserControl>
```

- [ ] **Step 2: Create `LandscapeView.xaml.cs`**

`OKXMonitor.Windows/Views/LandscapeView.xaml.cs` — reuses the same row/grid builders as portrait. Row 2 flattens positions + an orders summary + watchlist into one horizontal strip per "page":
```csharp
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OKXMonitor.Models;
using OKXMonitor.Services;
using OKXMonitor.Utils;

namespace OKXMonitor.Views;

public partial class LandscapeView : UserControl
{
    Store? _store;
    static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59));
    static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
    static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
    static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));

    public LandscapeView() { InitializeComponent(); }

    public void Bind(Store store)
    {
        _store = store;
        Row2Carousel.RowFactory = o => (UIElement)o; // items are pre-built strips
        Render();
    }

    static Brush PnlColor(double v) => v > 0 ? Green : v < 0 ? Red : Gray;

    public void Render()
    {
        if (_store is null) return;
        EquityText.Text = Format.Money(_store.Balance?.TotalEq);
        MarginText.Text = double.TryParse(_store.Balance?.MgnRatio, out var mr) ? "保证金率 " + Format.Percent(mr) : "";
        UplText.Text = Format.SignedMoney(_store.TotalUpl);
        UplText.Foreground = PnlColor(_store.TotalUpl);

        BuildPnlRows();
        BuildFeeLine();
        Row2Carousel.ItemsSource = BuildRow2Pages();
    }

    void BuildPnlRows()
    {
        for (int i = PnlGrid.Children.Count - 1; i >= 0; i--)
            if (Grid.GetRow(PnlGrid.Children[i]) > 0) PnlGrid.Children.RemoveAt(i);
        AddPnlRow(1, "今", _store!.PnlToday);
        AddPnlRow(2, "周", _store.PnlWeek);
        AddPnlRow(3, "月", _store.PnlMonth);
    }

    void AddPnlRow(int row, string label, PnLStats s)
    {
        Add(row, 0, label, Gray);
        Add(row, 1, Format.SignedMoney(s.Profit), s.Profit > 0 ? Green : Gray);
        Add(row, 2, Format.SignedMoney(s.Loss), s.Loss < 0 ? Red : Gray);
        Add(row, 3, Format.SignedMoney(s.Fee), s.Fee < 0 ? Orange : Gray);
        Add(row, 4, Format.SignedMoney(s.Net), PnlColor(s.Net));
    }

    void Add(int row, int col, string text, Brush color)
    {
        var tb = new TextBlock { Text = text, Foreground = color, FontSize = 9,
            FontFamily = new FontFamily("Consolas"), Margin = new Thickness(6, 0, 0, 0),
            TextAlignment = col == 0 ? TextAlignment.Left : TextAlignment.Right };
        Grid.SetRow(tb, row); Grid.SetColumn(tb, col);
        PnlGrid.Children.Add(tb);
    }

    void BuildFeeLine()
    {
        var parts = new List<string>();
        if (_store!.TradeFee?.MakerPct is { } mk) parts.Add("Mk " + Format.Rate(mk));
        if (_store.TradeFee?.TakerPct is { } tk) parts.Add("Tk " + Format.Rate(tk));
        if (_store.EffectiveFeePct is { } eff) parts.Add("月 " + Format.Rate(eff));
        FeeRateLine.Text = string.Join(" · ", parts);
    }

    // Row 2 packs positions + an orders chip + watch chips into pages of ~4 chips each.
    List<UIElement> BuildRow2Pages()
    {
        var chips = new List<UIElement>();
        foreach (var p in _store!.Positions) chips.Add(PositionChip(p));
        if (_store.Orders.Count > 0) chips.Add(Chip($"挂单 {_store.Orders.Count}", Gray));
        foreach (var w in _store.Watchlist) chips.Add(WatchChip(w));

        const int perPage = 4;
        var pages = new List<UIElement>();
        for (int i = 0; i < chips.Count; i += perPage)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Height = 20 };
            for (int j = i; j < System.Math.Min(i + perPage, chips.Count); j++) sp.Children.Add(chips[j]);
            pages.Add(sp);
        }
        if (pages.Count == 0) pages.Add(new TextBlock { Text = "无持仓 / 挂单", Foreground = Gray, FontSize = 10, Height = 20 });
        return pages;
    }

    UIElement PositionChip(Position p)
    {
        double upl = p.UplValue;
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        sp.Children.Add(new TextBlock { Text = Format.Symbol(p.InstId), FontSize = 11, FontWeight = FontWeights.SemiBold });
        var side = (p.PosSide ?? "").ToLowerInvariant();
        sp.Children.Add(new TextBlock { Text = side == "long" ? " 多" : side == "short" ? " 空" : "",
            Foreground = side == "long" ? Green : Red, FontSize = 10, Margin = new Thickness(2, 0, 4, 0) });
        sp.Children.Add(new TextBlock { Text = Format.SignedMoney(upl), Foreground = PnlColor(upl), FontSize = 11, FontFamily = new FontFamily("Consolas") });
        return sp;
    }

    UIElement WatchChip(WatchedToken w)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        sp.Children.Add(new TextBlock { Text = Format.Symbol(w.InstId), FontSize = 11 });
        sp.Children.Add(new TextBlock { Text = " " + (w.Change24h is { } c ? Format.Percent(c, true) : "—"),
            Foreground = w.Change24h is { } x ? PnlColor(x) : Gray, FontSize = 10, FontFamily = new FontFamily("Consolas") });
        return sp;
    }

    UIElement Chip(string text, Brush color) =>
        new TextBlock { Text = text, Foreground = color, FontSize = 11, Margin = new Thickness(0, 0, 12, 0) };
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build OKXMonitor.Windows`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```powershell
git add OKXMonitor.Windows/Views/LandscapeView.xaml OKXMonitor.Windows/Views/LandscapeView.xaml.cs
git commit -m "feat: landscape 2-row view with full PnL grid"
```

---

### Task 19: Layout toggle + per-layout geometry in `MainWindow`

**Files:**
- Modify: `OKXMonitor.Windows/MainWindow.xaml` (add ⤢ button)
- Modify: `OKXMonitor.Windows/MainWindow.xaml.cs`

- [ ] **Step 1: Add the toggle button to the header**

In `MainWindow.xaml`, add before `RefreshBtn`:
```xml
                    <Button x:Name="ToggleBtn" Content="⤢" Click="Toggle_Click" Style="{StaticResource Chrome}"/>
```

- [ ] **Step 2: Implement layout switching with per-layout geometry**

In `MainWindow.xaml.cs`: add a `_landscape` field and a `_current` layout field; route binding/rendering and geometry to the active layout.
```csharp
    readonly Views.LandscapeView _landscape = new();
    LayoutKind _current;

    // In constructor, after BodyHost set-up, replace fixed Portrait restore with:
    _current = _settings.LastLayout;
    ApplyLayout(_current, restore: true);

    void ApplyLayout(LayoutKind kind, bool restore)
    {
        _current = kind;
        if (kind == LayoutKind.Portrait)
        {
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            Width = 340;
            BodyHost.Content = _portrait;
        }
        else
        {
            SizeToContent = SizeToContent.Manual;
            ResizeMode = ResizeMode.CanResize;   // width draggable
            BodyHost.Content = _landscape;
        }
        if (restore) RestoreGeometry(kind);
        Render();
    }

    void Toggle_Click(object sender, RoutedEventArgs e)
    {
        SaveGeometry(_current);
        var next = _current == LayoutKind.Portrait ? LayoutKind.Landscape : LayoutKind.Portrait;
        _settings.LastLayout = next;
        _settings.Save();
        ApplyLayout(next, restore: true);
    }
```
Replace the old `RestoreGeometry()`/`OnClosing` with layout-aware versions:
```csharp
    void RestoreGeometry(LayoutKind kind)
    {
        var g = _settings.GeometryFor(kind);
        if (g.Width is { } w && kind == LayoutKind.Landscape) Width = w;
        if (g.Height is { } h && kind == LayoutKind.Landscape) Height = h;
        if (g.Left is { } l && g.Top is { } t) { Left = l; Top = t; EnsureOnScreen(); }
        else { var a = SystemParameters.WorkArea; Left = a.Right - Width - 20; Top = a.Top + 20; }
    }

    void SaveGeometry(LayoutKind kind)
    {
        var g = _settings.GeometryFor(kind);
        g.Left = Left; g.Top = Top; g.Width = Width; g.Height = Height;
        _settings.Save();
    }

    void OnClosing(object? sender, CancelEventArgs e) => SaveGeometry(_current);
```
Update `Render()` to bind/render the active view:
```csharp
        if (!_store.NeedsSetup)
        {
            if (_current == LayoutKind.Portrait) _portrait.Bind(_store);
            else _landscape.Bind(_store);
        }
```

- [ ] **Step 3: Build + run; verify toggle**

Run: `dotnet build OKXMonitor.sln; if ($?) { dotnet run --project OKXMonitor.Windows }`
Expected: clicking ⤢ swaps portrait↔landscape; each remembers its own size/position across toggles and restarts; landscape width is draggable.

- [ ] **Step 4: Commit**

```powershell
git add OKXMonitor.Windows/MainWindow.xaml OKXMonitor.Windows/MainWindow.xaml.cs
git commit -m "feat: layout toggle with per-layout geometry persistence"
```

---

### Task 20: Tray icon with dynamic uPnL number

**Files:**
- Create: `OKXMonitor.Windows/Utils/TrayIconRenderer.cs`
- Create: `OKXMonitor.Windows/Services/TrayIcon.cs`
- Modify: `OKXMonitor.Windows/OKXMonitor.Windows.csproj` (`UseWindowsForms` for NotifyIcon)
- Modify: `OKXMonitor.Windows/App.xaml.cs` (create/dispose tray), `MainWindow.xaml.cs` (expose toggle + show/hide)

- [ ] **Step 1: Enable WinForms (NotifyIcon) + System.Drawing**

In `OKXMonitor.Windows.csproj` `<PropertyGroup>` add:
```xml
    <UseWindowsForms>true</UseWindowsForms>
```
(`System.Drawing.Common` ships with the Windows desktop SDK; no NuGet needed.)

- [ ] **Step 2: Implement the icon renderer (GDI text→icon)**

`OKXMonitor.Windows/Utils/TrayIconRenderer.cs`:
```csharp
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace OKXMonitor.Utils;

/// Renders a short uPnL string into a 32x32 tray icon, colored by sign.
/// Mirrors the macOS menu-bar live-PnL number.
public static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Render(string text, Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            // Fit font to text length so longer numbers stay readable.
            float size = text.Length <= 4 ? 14f : text.Length <= 6 ? 11f : 9f;
            using var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, brush, new RectangleF(0, 0, 32, 32), fmt);
        }
        IntPtr h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }
}
```

- [ ] **Step 3: Implement the tray service**

`OKXMonitor.Windows/Services/TrayIcon.cs`:
```csharp
using System;
using System.Drawing;
using System.Windows.Forms;
using OKXMonitor.Utils;

namespace OKXMonitor.Services;

/// System-tray icon showing live uPnL; right-click menu mirrors the macOS dropdown.
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon = new();
    Icon? _current;

    public event Action? ToggleWindowRequested;
    public event Action? ToggleLayoutRequested;
    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    public TrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示/隐藏窗口", null, (_, _) => ToggleWindowRequested?.Invoke());
        menu.Items.Add("切换布局", null, (_, _) => ToggleLayoutRequested?.Invoke());
        menu.Items.Add("立即刷新", null, (_, _) => RefreshRequested?.Invoke());
        menu.Items.Add("设置…", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => QuitRequested?.Invoke());
        _icon.ContextMenuStrip = menu;
        _icon.Visible = true;
        _icon.DoubleClick += (_, _) => ToggleWindowRequested?.Invoke();
        Set(" OKX", Color.Gray, "OKX Monitor");
    }

    /// Update the icon from store state. Call on the UI thread after each refresh.
    public void Update(Store store)
    {
        if (store.NeedsSetup) { Set("OKX", Color.Gray, "未配置 API 凭据"); return; }
        if (store.LastUpdated is null && store.ErrorMessage is not null) { Set("--", Color.Gray, store.ErrorMessage); return; }
        double pnl = store.TotalUpl;
        var color = pnl > 0 ? Color.FromArgb(0x34, 0xC7, 0x59) : pnl < 0 ? Color.FromArgb(0xFF, 0x3B, 0x30) : Color.White;
        string text = (pnl > 0 ? "+" : "") + pnl.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        Set(text, color, "未实现盈亏 " + Format.SignedMoney(pnl));
    }

    void Set(string text, Color color, string tip)
    {
        var next = TrayIconRenderer.Render(text, color);
        _icon.Icon = next;
        _current?.Dispose();
        _current = next;
        _icon.Text = tip.Length > 63 ? tip[..63] : tip; // NotifyIcon tooltip cap
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _current?.Dispose();
    }
}
```

- [ ] **Step 4: Wire the tray into `App` + `MainWindow`**

In `MainWindow.xaml.cs` add public methods the tray calls:
```csharp
    public void ToggleVisibility() { if (IsVisible) Hide(); else { Show(); Activate(); } }
    public void ToggleLayoutPublic() => Toggle_Click(this, new RoutedEventArgs());
    public void RefreshPublic() => _ = _store.RefreshAsync();
    public void OpenSettingsPublic() => Settings_Click(this, new RoutedEventArgs());
    public Store StoreRef => _store;
```
In `App.xaml.cs`, after creating the window:
```csharp
        var tray = new Services.TrayIcon();
        tray.ToggleWindowRequested += () => window.ToggleVisibility();
        tray.ToggleLayoutRequested += () => window.ToggleLayoutPublic();
        tray.RefreshRequested += () => window.RefreshPublic();
        tray.SettingsRequested += () => window.OpenSettingsPublic();
        tray.QuitRequested += () => Shutdown();
        _store.PropertyChanged += (_, _) => Dispatcher.Invoke(() => tray.Update(_store));
        tray.Update(_store);
        Exit += (_, _) => tray.Dispose();
```
Store the `tray` in a field so it isn't GC'd. Add `using System.Windows.Threading;` if needed.

- [ ] **Step 5: Build + run; verify tray**

Run: `dotnet build OKXMonitor.sln; if ($?) { dotnet run --project OKXMonitor.Windows }`
Expected: a tray icon shows the live uPnL number (green/red), updates each refresh; right-click menu toggles window/layout, refreshes, opens settings, quits; double-click toggles the window.

- [ ] **Step 6: Commit**

```powershell
git add OKXMonitor.Windows/Utils/TrayIconRenderer.cs OKXMonitor.Windows/Services/TrayIcon.cs OKXMonitor.Windows/OKXMonitor.Windows.csproj OKXMonitor.Windows/App.xaml.cs OKXMonitor.Windows/MainWindow.xaml.cs
git commit -m "feat: tray icon with dynamic uPnL number + menu"
```

---

## Phase D — Packaging

### Task 21: Single-file publish + README

**Files:**
- Create: `OKXMonitor.Windows/README-windows.md`

- [ ] **Step 1: Publish a single-file self-contained EXE**

Run:
```powershell
dotnet publish OKXMonitor.Windows -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
Expected: `OKXMonitor.Windows\bin\Release\net8.0-windows\win-x64\publish\OKXMonitor.exe` (~70MB).

- [ ] **Step 2: Smoke-test the published EXE**

Run the published `OKXMonitor.exe`. Expected: launches identically to `dotnet run`; widget appears, tray works, no .NET install required.

- [ ] **Step 3: Write `README-windows.md`**

Document: requirements (Windows 10/11 x64), how to run/publish, the read-only-key reminder, credential storage location (Credential Manager), the SmartScreen "More info → Run anyway" note for the unsigned EXE, and the ⤢ layout toggle.

- [ ] **Step 4: Run full test suite + commit**

```powershell
dotnet test OKXMonitor.sln
git add OKXMonitor.Windows/README-windows.md
git commit -m "docs: Windows build/run/publish README"
```

---

## Acceptance criteria (verify before declaring done)

- [ ] First launch shows the "configure credentials" prompt; after saving, values persist across restarts.
- [ ] Credentials live only in Windows Credential Manager (visible under "Manage Windows Credentials"); none appear in `%AppData%\OKXMonitor\settings.json` or any log.
- [ ] Window is borderless, semi-transparent, always-on-top, draggable, not in taskbar, not in Alt-Tab.
- [ ] Both layouts render all data live; ⤢ (header) and the tray menu both toggle; each layout restores its own position/size, clamped on-screen.
- [ ] Realized-PnL 今日 updates within seconds of a closed trade today (dedup works).
- [ ] Fee row shows official Maker/Taker + month effective rate.
- [ ] Tray icon shows live uPnL (green/red); switching host in Settings takes effect on the next refresh.
- [ ] Killing the network ~10s shows an inline error naming the failed endpoint path, then recovers.
- [ ] Single-file EXE runs on a clean Windows 10/11 x64 box with no .NET installed.
- [ ] `dotnet test OKXMonitor.sln` is green (signing, candles, PnL bucketing, period math, effective fee, dedup, watchlist).
