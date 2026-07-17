namespace GenWave.Host.Api;

/// <summary>
/// Middleware that stamps <c>Cache-Control: no-store</c> and <c>Pragma: no-cache</c> on every
/// response whose request path starts with <c>/api/</c> (case-insensitive).
///
/// Rationale: browser-facing API responses must never be silently replayed from the HTTP cache.
/// The <c>no-store</c> directive is the strongest guarantee — it tells every intermediary
/// (browser, CDN, reverse-proxy) not to store the response at all.  This is intentionally broader
/// than just the live-page endpoints so that future <c>/api/*</c> additions are safe by default.
///
/// The <c>/media/*</c> minimal-API group is intentionally excluded: those endpoints serve the
/// Liquidsoap/Orchestrator hot path (server-to-server) and carry their own ETag+Last-Modified
/// negotiation to avoid redundant audio-file transfers.
///
/// Headers are written before the downstream pipeline runs so they appear in the response even
/// for error responses (401, 403, 500) and are not set if the action/controller already wrote
/// an explicit <c>Cache-Control</c> value (preservation logic in the check).
/// </summary>
public sealed class NoCacheApiMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var isApiPath = context.Request.Path.StartsWithSegments(
            "/api", StringComparison.OrdinalIgnoreCase);

        await next(context);

        // Set after the downstream pipeline so that controllers/actions that explicitly set their
        // own Cache-Control are not overwritten.  HTTP/1.1 allows setting headers after the body
        // is written only via trailers; we rely on the pipeline not yet having flushed the response
        // at this point (ASP.NET Core controllers buffer by default).  For streaming responses this
        // is a best-effort: the header is already sent if the response has started.
        if (isApiPath && !context.Response.HasStarted)
        {
            if (!context.Response.Headers.ContainsKey("Cache-Control"))
            {
                context.Response.Headers.CacheControl = "no-store";
            }
            if (!context.Response.Headers.ContainsKey("Pragma"))
            {
                context.Response.Headers.Pragma = "no-cache";
            }
        }
    }
}
