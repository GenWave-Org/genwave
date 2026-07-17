// STORY-124 — EndpointUri.Combine preserves a subpath in the configured base endpoint
//
// BDD specification — xUnit. Reviewer finding on the Story124 endpoint-liveness pass: the
// repoint specs (Story124_EndpointLiveRepoint.cs) only exercise subpath-free bases
// (http://host:port), so EndpointUri.Combine's actual reason for existing — a plain
// `new Uri(base, "/v1/...")` silently drops a base subpath like /openai — had no direct
// unit-level pin. This file is that pin.
// See docs/PLAN.md Epic T.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureEndpointUriJoin
{
    public sealed class ScenarioCombine
    {
        [Theory]
        // (1) bare host + rooted relative path — the common case (Tts:Endpoint default shape).
        [InlineData("http://kokoro:8880", "/v1/audio/speech", "http://kokoro:8880/v1/audio/speech")]
        // (2) a base carrying its own subpath — the exact bug this helper fixes: a plain
        // new Uri(base, "/v1/chat/completions") treats the relative arg as an absolute path and
        // discards "/openai" entirely. EndpointUri.Combine must not.
        [InlineData("https://host/openai", "/v1/chat/completions", "https://host/openai/v1/chat/completions")]
        // (3) a trailing slash on the base must not produce a doubled slash at the join point.
        [InlineData("http://kokoro:8880/", "/v1/audio/voices", "http://kokoro:8880/v1/audio/voices")]
        // (3b) trailing slash on the base AND no leading slash on the relative path — same result.
        [InlineData("http://kokoro:8880/", "v1/audio/voices", "http://kokoro:8880/v1/audio/voices")]
        // (2b) a subpath base with its own trailing slash, joined against a relative path with no
        // leading slash — still exactly one slash at the seam, subpath preserved.
        [InlineData("https://host/openai/", "v1/chat/completions", "https://host/openai/v1/chat/completions")]
        public void ProducesTheExpectedAbsoluteUri(string baseEndpoint, string relativePath, string expected)
        {
            var result = EndpointUri.Combine(baseEndpoint, relativePath);

            Assert.Equal(expected, result.ToString());
        }

        [Theory]
        // (4) no double slashes anywhere past the scheme separator, across every base/relative
        // slash combination a live operator-entered endpoint could produce.
        [InlineData("http://kokoro:8880", "/v1/audio/speech")]
        [InlineData("http://kokoro:8880/", "/v1/audio/speech")]
        [InlineData("http://kokoro:8880/", "v1/audio/speech")]
        [InlineData("https://host/openai", "/v1/chat/completions")]
        [InlineData("https://host/openai/", "/v1/chat/completions")]
        public void NeverProducesADoubleSlashAfterTheScheme(string baseEndpoint, string relativePath)
        {
            var result = EndpointUri.Combine(baseEndpoint, relativePath);

            // Strip the "scheme://" prefix before checking — that's the one legitimate "//" in
            // any of these URIs — then confirm no doubled path separator remains.
            var afterScheme = result.ToString().Split("://", 2)[1];

            Assert.DoesNotContain("//", afterScheme);
        }
    }
}
