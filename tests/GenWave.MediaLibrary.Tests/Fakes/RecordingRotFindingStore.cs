using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// Records the last <see cref="ReconcileUnreachableAsync"/> call's own envelope list, never touching
/// Postgres at all (SPEC F153.8; STORY-378; PLAN T376) — <c>UnreachableGardenerPass</c>'s own dedup
/// fact proves "the pass hands the repository ONE tuple" through this double rather than a real
/// <c>library.rot_finding</c> read. Every other <see cref="IRotFindingStore"/> member throws
/// <see cref="NotSupportedException"/> — the pass never calls them.
/// </summary>
public sealed class RecordingRotFindingStore : IRotFindingStore
{
    public IReadOnlyList<EnvelopeTuple>? ReceivedEnvelopes { get; private set; }

    public Task ReconcileUnreachableAsync(IReadOnlyList<EnvelopeTuple> envelopes, CancellationToken ct)
    {
        ReceivedEnvelopes = envelopes;
        return Task.CompletedTask;
    }

    public Task ReconcileDeadFilesAsync(TimeSpan unavailableGrace, CancellationToken ct) =>
        throw new NotSupportedException("RecordingRotFindingStore only records ReconcileUnreachableAsync.");

    public Task ReconcileNearDuplicatesAsync(int toleranceMs, CancellationToken ct) =>
        throw new NotSupportedException("RecordingRotFindingStore only records ReconcileUnreachableAsync.");

    public Task ReconcileStaleMetadataAsync(CancellationToken ct) =>
        throw new NotSupportedException("RecordingRotFindingStore only records ReconcileUnreachableAsync.");

    public Task ReconcileShelfDustAsync(TimeSpan shelfAge, CancellationToken ct) =>
        throw new NotSupportedException("RecordingRotFindingStore only records ReconcileUnreachableAsync.");

    public Task OpenDeadFileAsync(long mediaId, string reason, CancellationToken ct) =>
        throw new NotSupportedException("RecordingRotFindingStore only records ReconcileUnreachableAsync.");

    public Task<bool> DismissAsync(long findingId, CancellationToken ct) =>
        throw new NotSupportedException("RecordingRotFindingStore only records ReconcileUnreachableAsync.");

    public Task<IReadOnlyList<RotFinding>> ListAsync(
        RotKind? kind, RotState? state, CancellationToken ct, int limit = 200, int offset = 0) =>
        throw new NotSupportedException("RecordingRotFindingStore only records ReconcileUnreachableAsync.");

    public Task<RotFindingPage> ListWithMediaAsync(
        RotKind? kind, RotState? state, int limit, int offset, CancellationToken ct) =>
        throw new NotSupportedException("RecordingRotFindingStore only records ReconcileUnreachableAsync.");

    public Task<IReadOnlyDictionary<RotKind, int>> CountOpenByKindAsync(CancellationToken ct) =>
        throw new NotSupportedException("RecordingRotFindingStore only records ReconcileUnreachableAsync.");
}
