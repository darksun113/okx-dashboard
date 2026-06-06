using System;
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
        var oldBeginMs = 0d;
        var handler = new FakeHttpMessageHandler()
            .On("orders-history-archive",
                """{"code":"0","msg":"","data":[{"ordId":"X","pnl":"1","fillTime":"100"},{"ordId":"Y","pnl":"2","fillTime":"100"}]}""")
            .On("orders-history",
                """{"code":"0","msg":"","data":[{"ordId":"X","pnl":"999","fillTime":"100"}]}""");
        var client = new OkxClient(Creds(), "www.okx.com", new HttpClient(handler));

        var orders = await client.OrdersHistoryAsync(oldBeginMs);

        var x = orders.Single(o => o.OrdId == "X");
        Assert.Equal("999", x.Pnl);
        Assert.Contains(orders, o => o.OrdId == "Y");
        Assert.Equal(2, orders.Count);
    }

    [Fact]
    public async Task OrdersHistory_never_sends_begin_param()
    {
        var handler = new FakeHttpMessageHandler()
            .On("orders-history", """{"code":"0","msg":"","data":[]}""");
        var client = new OkxClient(Creds(), "www.okx.com", new HttpClient(handler));

        await client.OrdersHistoryAsync(0);

        Assert.NotEmpty(handler.Requests);
        Assert.All(handler.Requests, url => Assert.DoesNotContain("begin=", url));
    }

    [Fact]
    public async Task OrdersHistory_stops_after_one_page_when_oldest_below_threshold()
    {
        var rows = string.Join(",", Enumerable.Range(0, 100)
            .Select(i => "{\"ordId\":\"o" + i + "\",\"pnl\":\"1\",\"fillTime\":\"50\"}"));
        var json = "{\"code\":\"0\",\"msg\":\"\",\"data\":[" + rows + "]}";
        var handler = new FakeHttpMessageHandler().On("orders-history", json);
        var client = new OkxClient(Creds(), "www.okx.com", new HttpClient(handler));

        double beginMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1 * 86_400_000;
        await client.OrdersHistoryAsync(beginMs);

        Assert.Single(handler.Requests);
    }
}
