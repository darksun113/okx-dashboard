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
                                          Dictionary<string, double> ctVals)
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
