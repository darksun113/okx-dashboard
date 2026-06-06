namespace OKXMonitor.Services;

/// Mirrors the Swift `OKXError` enum: distinguishes a real network/transport
/// failure from an OKX API error (code != "0") from a bare HTTP status, and
/// from missing credentials. Messages are user-facing and never contain secrets.
public sealed class OkxException : Exception
{
    public enum Kind { Http, Api, Transport, MissingCredentials }

    public Kind ErrorKind { get; }

    OkxException(Kind kind, string message) : base(message) => ErrorKind = kind;

    public static OkxException Http(string path, int status) =>
        new(Kind.Http, $"HTTP {status} — {ShortPath(path)}");

    public static OkxException Api(string code, string msg) =>
        new(Kind.Api, $"OKX {code}: {msg}");

    public static OkxException Transport(string path, Exception inner) =>
        new(Kind.Transport, $"{inner.Message} — {ShortPath(path)}");

    public static OkxException MissingCredentials() =>
        new(Kind.MissingCredentials, "未配置 API 凭据");

    /// Drop the query string so the message stays compact in the footer.
    static string ShortPath(string p) => p.Split('?')[0];
}
