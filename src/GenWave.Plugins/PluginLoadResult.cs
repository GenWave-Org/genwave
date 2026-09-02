namespace GenWave.Plugins;

using GenWave.Core.Abstractions;

/// <summary>
/// Everything <see cref="PluginLoader.LoadAll"/> produced from one full pass over a plugins root (SPEC
/// F156.7, STORY-385/386): every plugin's <see cref="PluginLoadReport"/> (loaded or skipped, one per
/// candidate directory <c>PluginManifestDiscovery</c> found), plus the COMMITTED registrations from
/// every plugin that loaded — the two things the wiring task (PLAN T394) needs, respectively, to
/// populate <c>GET /api/status</c>'s <c>plugins[]</c> array and to hand real implementations into the
/// host's own <c>ContextPipeline</c>/<c>AdSpotPipeline</c> construction. A skipped plugin contributes
/// to neither list beyond its own report (STORY-385 AC8's no-partial guarantee).
/// </summary>
public sealed class PluginLoadResult
{
    public PluginLoadResult(
        IReadOnlyList<PluginLoadReport> reports,
        IReadOnlyList<ValidatedContextProvider> contextProviders,
        IReadOnlyList<IAdSpotSource> adSpotSources)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(contextProviders);
        ArgumentNullException.ThrowIfNull(adSpotSources);

        Reports = reports;
        ContextProviders = contextProviders;
        AdSpotSources = adSpotSources;
    }

    /// <summary>One entry per candidate <see cref="PluginManifestDiscovery.EnumerateCandidates"/>
    /// yielded, in the same ascending-slug order — loaded or skipped, never omitted.</summary>
    public IReadOnlyList<PluginLoadReport> Reports { get; }

    /// <summary>Every <see cref="IContextProvider"/> committed by a plugin that loaded, PAIRED with
    /// the exact key string <see cref="PluginLoader"/> already validated for it (T394 review HIGH-2 —
    /// see <see cref="ValidatedContextProvider"/>'s own remarks for why a bare provider list is unsafe
    /// here), in plugin-then-registration order, already pre-validated against key collisions
    /// (<see cref="PluginLoadFailureReason.ContextProviderKeyCollision"/>/
    /// <see cref="PluginLoadFailureReason.ContextProviderKeyInvalid"/>) — safe to hand straight into
    /// <c>ContextPipeline</c>'s constructor alongside the built-ins; that constructor's own fail-fast
    /// duplicate-key check is never expected to fire on any of these (F156.6), PROVIDED the caller
    /// reads <see cref="ValidatedContextProvider.ValidatedKey"/> rather than the wrapped provider's
    /// own <c>Key</c> getter a second time.</summary>
    public IReadOnlyList<ValidatedContextProvider> ContextProviders { get; }

    /// <summary>Every <see cref="IAdSpotSource"/> committed by a plugin that loaded, in
    /// plugin-then-registration order.</summary>
    public IReadOnlyList<IAdSpotSource> AdSpotSources { get; }
}
