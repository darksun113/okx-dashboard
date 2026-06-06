using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OKXMonitor.Services;

/// Pure OKX request signing. Extracted from OKXClient.swift's `sign` so it can be
/// unit-tested in isolation. No I/O, no secrets retained.
public static class OkxSigner
{
    /// Base64( HMAC-SHA256( secret, timestamp + method + path + body ) ).
    public static string Sign(string secret, string timestamp, string method, string path, string body = "")
    {
        var prehash = timestamp + method + path + body;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(prehash));
        return Convert.ToBase64String(mac);
    }

    /// ISO-8601 UTC with milliseconds, e.g. 2026-06-06T03:14:15.123Z.
    public static string TimestampUtc(DateTime utcNow) =>
        utcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}
