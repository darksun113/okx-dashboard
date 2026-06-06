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
