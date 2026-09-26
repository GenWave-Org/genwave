using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// A controllable <see cref="IAuthoredCatalogWriter"/> wrapping a REAL writer (gh-#854) — every call
/// against a media id not named in <see cref="DeclineMediaIds"/>/<see cref="ThrowMediaIds"/> delegates
/// straight through to <paramref name="inner"/>, so a spec isolating
/// <c>AdSpotRepository.SwapRenderedMediaAsync</c>'s own post-commit best-effort flip failure still runs
/// every OTHER write against real Postgres. Lets a spec observe <c>pending_retire_media_id</c>'s own
/// stamp (db/48's guarded UPDATE, landed inside the swap's own transaction) independently of whether
/// the flip that would otherwise clear it in the same call ever lands.
/// </summary>
public sealed class FakeAuthoredCatalogWriter(IAuthoredCatalogWriter inner) : IAuthoredCatalogWriter
{
    /// <summary>Media ids for which <see cref="SetEligibleAsync"/> reports <see langword="false"/>
    /// (mirrors the real writer's own "no matching row" outcome) rather than reaching
    /// <paramref name="inner"/>.</summary>
    public HashSet<long> DeclineMediaIds { get; } = [];

    /// <summary>Media ids for which <see cref="SetEligibleAsync"/> throws instead of returning.</summary>
    public HashSet<long> ThrowMediaIds { get; } = [];

    public List<(long MediaId, bool Eligible)> Calls { get; } = [];

    public Task<bool> SetEligibleAsync(long mediaId, bool eligible, CancellationToken ct)
    {
        Calls.Add((mediaId, eligible));
        if (ThrowMediaIds.Contains(mediaId))
            throw new InvalidOperationException($"Injected failure flipping media {mediaId} eligible={eligible}.");
        if (DeclineMediaIds.Contains(mediaId))
            return Task.FromResult(false);
        return inner.SetEligibleAsync(mediaId, eligible, ct);
    }

    public Task<long> InsertAuthoredAsync(AuthoredMediaInsert insert, CancellationToken ct) =>
        inner.InsertAuthoredAsync(insert, ct);
}
