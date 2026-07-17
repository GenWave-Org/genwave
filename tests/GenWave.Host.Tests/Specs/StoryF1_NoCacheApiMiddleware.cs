// F1 (bug) — Cache-Control: no-store on all /api/* responses
//
// BDD specification — xUnit.
//
// Tests construct NoCacheApiMiddleware directly with a DefaultHttpContext so they run fully
// in-process without a live stack or WebApplicationFactory.  The middleware sets headers
// after the downstream pipeline completes (no OnStarting callback), so the assertions can
// read headers directly from ctx.Response.Headers.

using Microsoft.AspNetCore.Http;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

public static class FeatureNoCacheApiMiddleware
{
    // ── Shared helper ─────────────────────────────────────────────────────

    /// <summary>
    /// Invokes the middleware against the given <paramref name="path"/> and returns the
    /// response headers so callers can assert.  An optional <paramref name="beforeNext"/>
    /// delegate runs inside the (fake) downstream — simulating a controller that already set
    /// its own headers.
    /// </summary>
    static async Task<IHeaderDictionary> RunAsync(
        string path,
        Action<HttpContext>? beforeNext = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;

        var middleware = new NoCacheApiMiddleware(innerCtx =>
        {
            beforeNext?.Invoke(innerCtx);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ctx);
        return ctx.Response.Headers;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioApiPathGetsNoStore
    {
        [Theory]
        [InlineData("/api/now-playing")]
        [InlineData("/api/play-history")]
        [InlineData("/api/media")]
        [InlineData("/api/settings")]
        [InlineData("/API/NOW-PLAYING")]   // case-insensitive match
        public async Task ApiPathResponseCarriesCacheControlNoStore(string path)
        {
            var headers = await RunAsync(path);
            Assert.Equal("no-store", headers.CacheControl.ToString());
        }

        [Theory]
        [InlineData("/api/now-playing")]
        [InlineData("/api/play-history")]
        [InlineData("/api/settings")]
        public async Task ApiPathResponseCarriesPragmaNoCache(string path)
        {
            var headers = await RunAsync(path);
            Assert.Equal("no-cache", headers.Pragma.ToString());
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioNonApiPathIsUntouched
    {
        [Theory]
        [InlineData("/health")]
        [InlineData("/media/some-file.mp3")]
        [InlineData("/")]
        [InlineData("/live")]
        public async Task NonApiPathDoesNotGetCacheControlHeader(string path)
        {
            var headers = await RunAsync(path);
            Assert.False(
                headers.ContainsKey("Cache-Control"),
                $"Expected no Cache-Control on '{path}' but found one.");
        }
    }

    public sealed class ScenarioExistingCacheControlIsPreserved
    {
        [Fact]
        public async Task PreExistingCacheControlHeaderIsNotOverwritten()
        {
            // Simulate an action (e.g. GET /api/media/{id} with ETag) that already
            // sets its own Cache-Control inside the downstream pipeline.
            var headers = await RunAsync(
                "/api/media/123",
                ctx => ctx.Response.Headers.CacheControl = "no-cache, must-revalidate");

            Assert.Equal("no-cache, must-revalidate", headers.CacheControl.ToString());
        }
    }
}
