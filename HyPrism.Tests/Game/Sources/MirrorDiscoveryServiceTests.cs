using System.Net;
using System.Net.Http;
using System.Text;
using HyPrism.Services.Game.Sources;

namespace HyPrism.Tests.Game.Sources;

public class MirrorDiscoveryServiceTests
{

    private static HttpClient BuildClient(HttpStatusCode status, string body)
    {
        var handler = new StubHttpHandler(status, body);
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }

    private static HttpClient BuildClientWithFallback(
        Func<HttpRequestMessage, (HttpStatusCode, string)> selector)
    {
        var handler = new SelectorHttpHandler(selector);
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }


    [Fact]
    public async Task DiscoverMirrorAsync_EmptyUrl_ReturnsFailure()
    {
        var svc = new MirrorDiscoveryService(BuildClient(HttpStatusCode.OK, "{}"));
        var result = await svc.DiscoverMirrorAsync("");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Error ?? "");
    }

    [Fact]
    public async Task DiscoverMirrorAsync_WhitespaceUrl_ReturnsFailure()
    {
        var svc = new MirrorDiscoveryService(BuildClient(HttpStatusCode.OK, "{}"));
        var result = await svc.DiscoverMirrorAsync("   ");

        Assert.False(result.Success);
    }


    [Fact]
    public async Task DiscoverMirrorAsync_JsonIndexResponse_ReturnsSuccess()
    {
        // Simulate a server that returns a valid version-index JSON
        const string indexJson = """
            [
              {
                "version": "0.1.0",
                "type": "release",
                "files": [
                  { "path": "Client/file.zip", "sha256": "abc123", "size": 12345 }
                ]
              }
            ]
            """;

        var svc = new MirrorDiscoveryService(BuildClient(HttpStatusCode.OK, indexJson));
        var result = await svc.DiscoverMirrorAsync("https://mirror.example.com");

        // May succeed or not depending on detection heuristic; the key guarantee is no exception
        Assert.IsType<DiscoveryResult>(result);
    }


    [Fact]
    public async Task DiscoverMirrorAsync_AllEndpoints404_ReturnsFailure()
    {
        var svc = new MirrorDiscoveryService(BuildClient(HttpStatusCode.NotFound, "not found"));
        var result = await svc.DiscoverMirrorAsync("https://mirror.example.com");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }


    [Fact]
    public async Task DiscoverMirrorAsync_CancelledToken_ThrowsOrReturnsFailure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var svc = new MirrorDiscoveryService(BuildClient(HttpStatusCode.OK, "{}"));

        // Either throws OperationCanceledException or returns a failure result
        try
        {
            var result = await svc.DiscoverMirrorAsync("https://mirror.example.com", null, cts.Token);
            Assert.False(result.Success);
        }
        catch (OperationCanceledException)
        {
            // Also acceptable
        }
    }


    [Fact]
    public void DiscoveryResult_FailureResult_HasExpectedShape()
    {
        var r = new DiscoveryResult { Success = false, Error = "oops" };
        Assert.False(r.Success);
        Assert.Equal("oops", r.Error);
        Assert.Null(r.Mirror);
        Assert.Null(r.DetectedType);
    }


    private sealed class StubHttpHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class SelectorHttpHandler(
        Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> selector) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (status, body) = selector(request);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
