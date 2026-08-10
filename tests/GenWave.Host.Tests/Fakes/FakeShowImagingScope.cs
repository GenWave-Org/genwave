using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IShowImagingScope"/> double (STORY-305, PLAN T240) for
/// <c>ShowsController</c>'s wire-layer specs — mirrors <see cref="FakeMediaLibraryMembership"/>'s own
/// posture: a fixed (show id → scoped rows) map, no re-implementation of
/// <c>GenWave.MediaLibrary.Catalog.ShowImagingScopeRepository</c>'s own SQL. The no-args construction
/// knows no rows — every unscope answer is the empty list, the "nothing was ever scoped to this show"
/// default every pre-existing spec would assume.
/// </summary>
sealed class FakeShowImagingScope(IReadOnlyDictionary<long, IReadOnlyList<ScopedImagingRow>>? scopedByShowId = null)
    : IShowImagingScope
{
    readonly IReadOnlyDictionary<long, IReadOnlyList<ScopedImagingRow>> scopedByShowId =
        scopedByShowId ?? new Dictionary<long, IReadOnlyList<ScopedImagingRow>>();

    /// <summary>Every showId this double's <see cref="UnscopeAsync"/> was actually called with, in
    /// call order — proves the show delete guard calls it AFTER a successful delete, never before or
    /// on a refused one (SPEC F115.4's own ordering rule).</summary>
    public List<long> UnscopeCalls { get; } = [];

    /// <summary>Scripts the NEXT <see cref="UnscopeAsync"/> call to throw this exception instead of
    /// returning (PLAN T240 review — proves <c>ShowsController.Delete</c>'s own best-effort posture:
    /// a library-connection failure here still reports the delete's own success, logged rather than
    /// surfaced as a 500). Cleared after one use; the call is still recorded in
    /// <see cref="UnscopeCalls"/> before the throw, matching a real repository call that fails mid
    /// round-trip after already being dispatched.</summary>
    public Exception? NextThrow { get; set; }

    public Task<IReadOnlyList<ScopedImagingRow>> UnscopeAsync(long showId, CancellationToken ct)
    {
        UnscopeCalls.Add(showId);

        if (NextThrow is { } toThrow)
        {
            NextThrow = null;
            throw toThrow;
        }

        return Task.FromResult(scopedByShowId.TryGetValue(showId, out var rows) ? rows : []);
    }
}
