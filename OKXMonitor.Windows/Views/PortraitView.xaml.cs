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
