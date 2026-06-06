using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

/// Returns canned JSON keyed by a substring of the request path. Records calls.
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    readonly List<(string contains, string json)> _routes = new();
    public List<string> Requests { get; } = new();

    public FakeHttpMessageHandler On(string pathContains, string json)
    {
        _routes.Add((pathContains, json));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.PathAndQuery;
        Requests.Add(url);
        foreach (var (contains, json) in _routes)
            if (url.Contains(contains))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json),
                });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"code":"0","msg":"","data":[]}"""),
        });
    }
}
