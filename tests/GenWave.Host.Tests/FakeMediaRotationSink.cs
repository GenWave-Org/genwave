using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests;

/// <summary>
/// Scriptable <see cref="IMediaRotationSink"/> double shared across specs that need
/// <see cref="GenWave.Host.Api.StatusController"/> constructed directly (mirrors
/// <see cref="FakeMediaCatalog"/>'s idiom). Defaults to an all-zero, no-epoch
/// <see cref="RotationHealth"/> so specs unrelated to SPEC F149.5 (PLAN T371) get a stable, inert
/// answer without scripting anything; <see cref="Health"/> is settable for the specs that do care.
/// <see cref="RecordAiringAsync"/>/<see cref="GetRotationSinceAsync"/>/<see cref="GetNeverAiredCountAsync"/>
/// are unreached by every current caller of this double (StatusController.Get only ever calls
/// <see cref="GetRotationHealthAsync"/>) — scripted to throw, so a future caller that starts reaching
/// them here fails loudly instead of silently returning a made-up value.
/// </summary>
sealed class FakeMediaRotationSink : IMediaRotationSink
{
    public RotationHealth Health { get; set; } = new(0, 0, 0, 0, null);

    public Task<RotationHealth> GetRotationHealthAsync(LibraryScope scope, CancellationToken ct) =>
        Task.FromResult(Health);

    public Task RecordAiringAsync(long mediaId, DateTimeOffset airedAt, CancellationToken ct) =>
        throw new NotSupportedException("unused by this double's current callers");

    public Task<DateTimeOffset?> GetRotationSinceAsync(CancellationToken ct) =>
        throw new NotSupportedException("unused by this double's current callers");

    public Task<long> GetNeverAiredCountAsync(CancellationToken ct) =>
        throw new NotSupportedException("unused by this double's current callers");
}
