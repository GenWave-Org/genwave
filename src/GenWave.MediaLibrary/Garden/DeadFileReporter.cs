using GenWave.Core.Abstractions;

namespace GenWave.MediaLibrary.Garden;

/// <summary>
/// <see cref="IDeadFileReporter"/>'s one implementation (SPEC F153.4; STORY-375; PLAN T373,
/// gh-#529) — Dapper-free itself (L2's own Garden narrowing, T357: only a <c>*Repository</c>-named
/// type in this namespace may touch Npgsql/Dapper): every statement lives in
/// <see cref="RotFindingRepository.OpenDeadFileAsync"/>, this type only supplies the
/// <c>push_missing</c> reason literal <c>Host.Engine.MediaExistencePushGuard</c>'s decline
/// warrants.
/// </summary>
sealed class DeadFileReporter(IRotFindingStore store) : IDeadFileReporter
{
    public Task ReportMissingAsync(long mediaId, CancellationToken ct) =>
        store.OpenDeadFileAsync(mediaId, "push_missing", ct);
}
