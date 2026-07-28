using GenWave.Core.Abstractions;

namespace GenWave.Core.Tests.Fakes;

/// <summary>
/// Hand-rolled <see cref="IArtworkUrlEchoValidator"/> double (PLAN T125 review F2/F4) — scripts a
/// trust predicate instead of standing up a real <c>StationOptions</c>/<c>IOptionsMonitor</c> stack.
/// </summary>
sealed class FakeArtworkUrlEchoValidator(Func<string, bool> predicate) : IArtworkUrlEchoValidator
{
    /// <summary>A validator that trusts every url whose prefix exactly matches <paramref name="trustedPrefix"/>.</summary>
    public static FakeArtworkUrlEchoValidator TrustingPrefix(string trustedPrefix) =>
        new(url => url.StartsWith(trustedPrefix, StringComparison.Ordinal));

    public bool IsTrusted(string url) => predicate(url);
}
