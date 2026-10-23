using Microsoft.AspNetCore.Http;

namespace GenWave.VoiceOff.Serving;

/// <summary>The headers every response carries. Inline script and style are allowed because the listening page is one self-contained file.</summary>
internal static class SecurityHeaders
{
    internal const string ContentSecurityPolicy =
        "default-src 'self'; media-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

    internal static void Apply(HttpResponse response)
    {
        response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.Append("Referrer-Policy", "no-referrer");
        response.Headers.CacheControl = "no-store";
    }
}
