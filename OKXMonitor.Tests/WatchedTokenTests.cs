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
