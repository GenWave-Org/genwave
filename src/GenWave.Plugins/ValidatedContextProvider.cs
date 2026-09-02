namespace GenWave.Plugins;

using GenWave.Core.Abstractions;

/// <summary>
/// One committed plugin <see cref="IContextProvider"/>, paired with the EXACT key string
/// <see cref="PluginLoader"/> already validated for it (SPEC F156.6; T392 review finding B2; T394
/// review HIGH-2) — never <see cref="IContextProvider.Key"/> read again downstream. A third-party
/// <c>Key</c> getter is not guaranteed to be pure — <see cref="IContextProvider.Key"/>'s own contract
/// calls the value "stable" but nothing enforces that — so a caller re-reading it after the loader
/// already validated a DIFFERENT answer could smuggle an unvalidated (unformatted, colliding) key
/// straight into <c>GenWave.Context.ContextPipeline</c>'s own fail-fast constructor: proven live at
/// T394 review (a drifting getter answering a built-in's own key on its second read froze that
/// SECOND, unvalidated answer into a naive memoizing wrapper, downing the station the instant
/// something resolved <c>ContextPipeline</c>). This record is what makes "never read again" true by
/// construction rather than by caller discipline: <see cref="PluginLoadResult.ContextProviders"/>
/// hands back these pairs, not bare providers, so every consumer — today, GenWave.Host's own plugin-
/// door wiring — has the validated identity in hand and never needs to touch <see cref="Provider"/>'s
/// own <c>Key</c> getter at all.
/// </summary>
/// <param name="ValidatedKey">The key <see cref="PluginLoader"/> already validated by format and
/// uniqueness (F156.6) — the value to use everywhere this provider's identity matters.</param>
/// <param name="Provider">The committed provider itself.</param>
public sealed record ValidatedContextProvider(string ValidatedKey, IContextProvider Provider);
