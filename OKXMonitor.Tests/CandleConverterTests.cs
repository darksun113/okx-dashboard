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
