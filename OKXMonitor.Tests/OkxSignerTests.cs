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
