using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace GenWave.Host.Api;

/// <summary>
/// Stamps <c>Cache-Control: public, max-age={maxAgeSeconds}</c> on an action's response (SPEC
/// F62.11, STORY-171/T13). Runs as an MVC result filter — inside the controller pipeline, which
/// sits downstream of the <c>OutputCache</c> middleware in Program.cs's pipeline order — so the
/// header is present on the very response the middleware captures. That is what makes both the
/// first request AND every subsequent cached hit within the TTL replay the same header: the
/// framework's output-cache middleware caches (and replays) the whole response, headers
/// included, but it does not set <c>Cache-Control</c> itself.
/// <para>
/// The <paramref name="maxAgeSeconds"/> passed here must match the paired
/// <c>[OutputCache(PolicyName = ...)]</c> attribute's TTL (see
/// <see cref="SpectatorOutputCachePolicies"/>) — the two express the same one contract to two
/// different caches (this process's own OutputCache store, and any external CDN/reverse-proxy
/// honoring the header) and must not drift apart.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SpectatorCacheControlAttribute(int maxAgeSeconds) : ResultFilterAttribute
{
    public override void OnResultExecuting(ResultExecutingContext context)
    {
        context.HttpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = TimeSpan.FromSeconds(maxAgeSeconds),
        };

        base.OnResultExecuting(context);
    }
}
