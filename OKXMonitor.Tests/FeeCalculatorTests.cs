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
