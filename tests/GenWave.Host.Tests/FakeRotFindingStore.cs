using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests;

/// <summary>
/// Scriptable <see cref="IRotFindingStore"/> double shared across specs that need
/// <see cref="GenWave.Host.Api.StatusController"/> constructed directly, or via a
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> over a bogus
/// library connection string (mirrors <see cref="FakeMediaRotationSink"/>'s idiom — PLAN T377).
/// Defaults to an empty open-counts dictionary so specs unrelated to SPEC F153.9 get a stable, inert
/// answer (every kind reads 0) without scripting anything; <see cref="OpenCounts"/> is settable for
/// the specs that do care. Every OTHER member is unreached by
/// <see cref="GenWave.Host.Api.StatusController.Get"/> (its own only call is
/// <see cref="CountOpenByKindAsync"/>) — scripted to throw, so a future caller that starts reaching
/// them here fails loudly instead of silently returning a made-up value.
/// </summary>
sealed class FakeRotFindingStore : IRotFindingStore
{
    public IReadOnlyDictionary<RotKind, int> OpenCounts { get; set; } = new Dictionary<RotKind, int>();

    public Task<IReadOnlyDictionary<RotKind, int>> CountOpenByKindAsync(CancellationToken ct) =>
        Task.FromResult(OpenCounts);

    public Task ReconcileDeadFilesAsync(TimeSpan unavailableGrace, CancellationToken ct) =>
        throw new NotSupportedException("unused by this double's current callers");

    public Task ReconcileNearDuplicatesAsync(int toleranceMs, CancellationToken ct) =>
        throw new NotSupportedException("unused by this double's current callers");

    public Task ReconcileStaleMetadataAsync(CancellationToken ct) =>
        throw new NotSupportedException("unused by this double's current callers");

    public Task ReconcileShelfDustAsync(TimeSpan shelfAge, CancellationToken ct) =>
        throw new NotSupportedException("unused by this double's current callers");

    public Task ReconcileUnreachableAsync(IReadOnlyList<EnvelopeTuple> envelopes, CancellationToken ct) =>
        throw new NotSupportedException("unused by this double's current callers");

    public Task OpenDeadFileAsync(long mediaId, string reason, CancellationToken ct) =>
        throw new NotSupportedException("unused by this double's current callers");

    public Task<bool> DismissAsync(long findingId, CancellationToken ct) =>
        throw new NotSupportedException("unused by this double's current callers");

    public Task<IReadOnlyList<RotFinding>> ListAsync(
        RotKind? kind, RotState? state, CancellationToken ct, int limit = 200, int offset = 0) =>
        throw new NotSupportedException("unused by this double's current callers");

    public Task<IReadOnlyList<RotFindingWithMedia>> ListWithMediaAsync(
        RotKind? kind, RotState? state, int limit, int offset, CancellationToken ct) =>
        throw new NotSupportedException("unused by this double's current callers");
}
