using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Selection;

/// <summary>
/// The v1 body of the selection seam (PRD §4.2 / §10, SEAM 1): ask the catalog for a random ready
/// track, excluding what aired recently, and narrow it to the feeder's <see cref="MediaItem"/>. This
/// is the entire selection logic in v1. Kept behind <see cref="INextItemProvider"/> ("ask the active
/// strategy") so a future strategy mechanism/config is a binding change, not a rewrite — but neither a
/// strategy registry nor configs are built yet (PRD §10 strategy discipline).
/// </summary>
sealed class RandomSelectionProvider(IMediaCatalog catalog, IStationScopeProvider scopeProvider) : INextItemProvider
{
    public async Task<MediaItem?> GetNextAsync(PlayoutContext ctx, CancellationToken ct)
    {
        // Read the live scope on every call (SPEC F30.1) — never store it in a field.
        var reference = await catalog.GetRandomReadyAsync(scopeProvider.Current, ctx.RecentMediaIds, ct);
        return reference?.ToMediaItem();
    }
}
