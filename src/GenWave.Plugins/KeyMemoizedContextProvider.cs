namespace GenWave.Plugins;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Freezes a committed plugin's <see cref="IContextProvider.Key"/> to the VALIDATED value the loader
/// already established — never a fresh read of the third-party getter (SPEC F156.6; T392 review
/// finding B2; T394 review HIGH-2). <c>PluginLoader</c> validates a plugin's key exactly once, by
/// value, pairing it with the committed provider in a <see cref="ValidatedContextProvider"/> (see that
/// type's own remarks on why a bare provider list is unsafe here) — this type's constructor takes THAT
/// validated string directly, rather than reading <see cref="IContextProvider.Key"/> itself.
///
/// <para>
/// <b>Proven live at T394 review (the bug this type exists to close).</b> An EARLIER shape of this
/// wrapper read <c>inner.Key</c> in its own constructor — a fresh, unvalidated read, once removed from
/// the loader's own validation but still a live call to a third-party getter. A drifting getter that
/// answered a SAFE key on the loader's own read, then a BUILT-IN's key ("weather") on this wrapper's
/// later read, froze the SECOND, never-validated answer — and <c>GenWave.Context.ContextPipeline</c>'s
/// own fail-fast duplicate-key constructor threw the instant anything resolved it (a background
/// service, an endpoint), downing the station on boot. Taking the validated string as a CONSTRUCTOR
/// ARGUMENT — never calling <c>.Key</c> at all — closes that gap by construction: the getter's own
/// answer, drifting or not, plays no further part once <c>PluginLoader</c> has committed.
/// </para>
///
/// <para>
/// <b>Deliberately NOT applied inside <c>PluginLoader</c> itself.</b> Two of that project's own
/// Plugins.Tests facts (<c>TheAssemblyLoadsInADedicatedLoadContext</c>,
/// <c>ScenarioLoadingTheExampleProjectsRealBuildOutput</c>) resolve
/// <c>pair.Provider.GetType().Assembly</c> against the COMMITTED provider to prove SPEC F156.3's own
/// AssemblyLoadContext isolation — wrapping at commit would substitute this type's own (host-loaded,
/// Default-context) assembly for the plugin's, silently breaking that proof. Wrapping happens here, one
/// layer up, in the Host composition root that actually registers providers into DI — see
/// <c>GenWave.Host.Api.PluginDoorServiceCollectionExtensions</c>.
/// </para>
///
/// <para>
/// <see cref="FetchAsync"/> is a plain, unmemoized delegation — nothing about a provider's FACTS needs
/// this treatment, only its identity.
/// </para>
/// </summary>
public sealed class KeyMemoizedContextProvider : IContextProvider
{
    readonly IContextProvider inner;

    /// <param name="validatedKey">The exact key <c>PluginLoader</c> already validated for
    /// <paramref name="inner"/> (<see cref="ValidatedContextProvider.ValidatedKey"/>) — never re-derived
    /// from <paramref name="inner"/>'s own <see cref="IContextProvider.Key"/> getter. Must be non-null
    /// and non-blank: the loader's own validation never hands back anything else, so a violation here
    /// signals a caller bug, not a runtime plugin misbehavior — fail fast rather than silently wrapping
    /// an invalid identity.</param>
    /// <param name="inner">The plugin-committed provider whose <see cref="FetchAsync"/> this delegates
    /// to. Its own <see cref="IContextProvider.Key"/> getter is never read by this type at all.</param>
    public KeyMemoizedContextProvider(string validatedKey, IContextProvider inner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(validatedKey);
        ArgumentNullException.ThrowIfNull(inner);

        Key = validatedKey;
        this.inner = inner;
    }

    public string Key { get; }

    public Task<ContextContent?> FetchAsync(CancellationToken ct) => inner.FetchAsync(ct);
}
