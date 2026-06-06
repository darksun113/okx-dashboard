# OKX Monitor — Windows

> 常驻置顶的桌面悬浮挂件，只读监控 OKX 永续合约账户。Windows 版（.NET 8 / WPF），
> 支持**竖屏面板**与 **21:9 横屏条**两种可切换布局。
>
> An always-on-top desktop widget for monitoring an OKX perpetual-swap account
> (read-only). Windows port (.NET 8 / WPF) with two switchable layouts —
> a portrait panel and a 21:9 landscape strip.

## 功能 · Features

- 🪟 无边框置顶悬浮窗，**不进任务栏、不进 Alt-Tab**，标题栏拖动 / Borderless always-on-top widget, hidden from taskbar & Alt-Tab, drag by the header.
- 🔄 **两种布局，一键切换**：竖屏（账户/已实现盈亏3×5网格/持仓/挂单轮播/关注轮播）与横屏 2 行宽条（含完整盈亏网格）。点标题栏 **⤢** 或托盘右键菜单切换；每种布局各自记住位置与大小。
- 📊 账户权益 / 保证金率 / 未实现盈亏实时刷新。
- 📈 已实现盈亏按 GMT+8 今日 / 本周 / 本月分桶，5 列：盈利 · 亏损 · 手续费 · 合计；并显示 Maker/Taker 官方费率 + 本月实际有效费率。
- 📋 持仓 · 挂单（轮播）· 自动关注列表（持仓∪挂单，2h/6h/24h 涨跌）。
- 🟰 **托盘图标实时显示未实现盈亏数字**（绿涨红跌），不开窗也能一眼看盈亏；右键菜单：显示/隐藏 · 切换布局 · 立即刷新 · 设置 · 退出。
- 🔒 凭据存入 **Windows 凭据管理器**（DPAPI 加密），从不写入文件 / 注册表 / 命令行 / 日志。
- 🛡️ 只读 API：无法下单 / 撤单 / 提现。全程 HTTPS，使用系统默认证书校验。
- 🌐 API 域名可在设置切换（`www.okx.cab` / `www.okx.com` / `aws.okx.com`），应对网络封锁。

## 环境 · Requirements

- Windows 10 / 11 x64。
- 运行已发布的单文件 EXE **无需安装 .NET**（self-contained）。
- 从源码构建需要 .NET SDK 8 或更高（本仓库用 9.0.x SDK 编译 `net8.0-windows`）。

## 构建与运行 · Build & Run

```powershell
# 开发运行 · Dev run
dotnet run --project OKXMonitor.Windows

# 单元测试 · Tests
dotnet test OKXMonitor.sln

# 生产单文件 EXE · Single-file self-contained EXE
dotnet publish OKXMonitor.Windows -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# 产物 · Output:
# OKXMonitor.Windows\bin\Release\net8.0-windows\win-x64\publish\OKXMonitor.exe
```

> 单文件 EXE 约 150 MB（包含完整 WPF/WinForms 运行时与原生库）。

### SmartScreen（未签名 EXE）

未做 Authenticode 代码签名时，首次运行可能弹出 Microsoft Defender SmartScreen 警告。
点 **More info → Run anyway**（更多信息 → 仍要运行）即可。若要分发，建议用代码签名证书签名。

## 首次配置 · First-time setup

1. OKX 网站 → 个人中心 → API → 创建 API key，**权限仅勾选「读取」**。
   On OKX: Profile → API → create a key with **READ permission only**.
2. 启动后点 widget 右上角 **⚙**，填入 API Key / Secret / Passphrase。模拟盘勾选「模拟盘 (Demo Trading)」。
3. `www.okx.cab` 连不上时，在「API 域名」切到 `www.okx.com` 或 `aws.okx.com`。

> ⚠️ 请务必使用**只读** key。本程序只发 GET 请求，无法交易或提现；但泄露的可交易 key 是财务灾难。

## 凭据存储 · Credential storage

凭据以单条 JSON 写入 **Windows 凭据管理器**（条目名 `OKXMonitor`，DPAPI 加密，按登录会话解锁）。
- 查看 / 删除：控制面板 →「凭据管理器」→「Windows 凭据」，或在设置里点「删除凭据」。
- 非密配置（API 域名 / 刷新间隔 / 当前布局 / 各布局窗口位置）存于
  `%AppData%\OKXMonitor\settings.json`，**不含任何密钥**。

## 开机自启（可选） · Auto-start (optional)

在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 加一项指向 `OKXMonitor.exe` 即可（仅当前用户）。

## 项目结构 · Project layout

```
OKXMonitor.Windows/
  App.xaml(.cs)            入口 · 单实例 Mutex · 托盘
  MainWindow.xaml(.cs)     置顶窗口 · 拖动 · 布局切换 · 每布局位置记忆
  Views/
    PortraitView           竖屏 5 区块
    LandscapeView          横屏 2 行（Viewbox 随窗口缩放）
    SettingsWindow         设置弹窗
    Controls/Carousel.cs   自动轮播控件
  Services/
    OkxClient.cs           签名 REST 客户端（全部 GET）
    OkxSigner.cs           HMAC-SHA256 签名（纯函数，已单测）
    Store.cs               状态 + 轮询（DispatcherTimer）
    CredentialStore.cs     Windows 凭据管理器
    AppSettings.cs         settings.json
    TrayIcon.cs            托盘图标 + 菜单
  Models/OkxResponses.cs   响应模型 + PnL/关注列表派生
  Utils/                   PeriodCalculator(GMT+8) · FeeCalculator · Format · TrayIconRenderer
OKXMonitor.Tests/          xUnit：签名 · K线解码 · PnL分桶 · 时间窗 · 有效费率 · 历史去重 · 关注列表
```

## 免责声明 · Disclaimer

本项目仅用于行情和账户监控，**只读、不下单**。请使用**仅勾选「读取」权限**的 API key。
作者不对因凭据泄露、第三方接口变更、网络故障等导致的任何损失负责。
