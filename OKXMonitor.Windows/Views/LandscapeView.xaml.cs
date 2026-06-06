using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OKXMonitor.Models;
using OKXMonitor.Services;
using OKXMonitor.Utils;

// Disambiguate WinForms vs WPF types brought in by UseWindowsForms.
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;
using UserControl = System.Windows.Controls.UserControl;

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
        MarginText.Text = double.TryParse(_store.Balance?.MgnRatio, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var mr) ? "保证金率 " + Format.Percent(mr) : "";
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
        var tb = new TextBlock { Text = text, Foreground = color, FontSize = 11,
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
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Height = 26 };
            for (int j = i; j < System.Math.Min(i + perPage, chips.Count); j++) sp.Children.Add(chips[j]);
            pages.Add(sp);
        }
        if (pages.Count == 0) pages.Add(new TextBlock { Text = "无持仓 / 挂单", Foreground = Gray, FontSize = 12, Height = 26 });
        return pages;
    }

    UIElement PositionChip(Position p)
    {
        double upl = p.UplValue;
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        sp.Children.Add(new TextBlock { Text = Format.Symbol(p.InstId), FontSize = 13, FontWeight = FontWeights.SemiBold });
        var side = (p.PosSide ?? "").ToLowerInvariant();
        sp.Children.Add(new TextBlock { Text = side == "long" ? " 多" : side == "short" ? " 空" : "",
            Foreground = side == "long" ? Green : Red, FontSize = 12, Margin = new Thickness(2, 0, 4, 0) });
        sp.Children.Add(new TextBlock { Text = Format.SignedMoney(upl), Foreground = PnlColor(upl), FontSize = 13, FontFamily = new FontFamily("Consolas") });
        return sp;
    }

    UIElement WatchChip(WatchedToken w)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        sp.Children.Add(new TextBlock { Text = Format.Symbol(w.InstId), FontSize = 13 });
        sp.Children.Add(new TextBlock { Text = " " + (w.Change24h is { } c ? Format.Percent(c, true) : "—"),
            Foreground = w.Change24h is { } x ? PnlColor(x) : Gray, FontSize = 12, FontFamily = new FontFamily("Consolas") });
        return sp;
    }

    UIElement Chip(string text, Brush color) =>
        new TextBlock { Text = text, Foreground = color, FontSize = 13, Margin = new Thickness(0, 0, 12, 0) };
}
