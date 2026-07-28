namespace GenWave.Core.Domain;

/// <summary>
/// The outcome of <see cref="Abstractions.ILiquidsoapControl.PushAsync"/> (SPEC F88.4, F93.3, PLAN
/// T125): the engine-assigned RID <see cref="Rid"/> (validated numeric by the implementation before
/// it is returned) alongside <see cref="ArtworkUrl"/> — the SAME <c>url=</c> annotation value the
/// push just stamped onto the queued item, handed back so <see cref="Playout.PlayoutFeeder"/> can
/// carry it into <c>pushedMeta</c> at push time rather than waiting for (or re-deriving) it later.
/// Threading it through the push's own return value keeps the feeder's "zero DB reads per poll"
/// contract (SPEC F16.6/F93.4) intact — the artwork URL was already computed once, by the caller of
/// <see cref="Abstractions.ILiquidsoapControl.PushAsync"/>, to build the annotation string itself.
/// </summary>
/// <param name="Rid">The engine's numeric request id for the pushed item.</param>
/// <param name="ArtworkUrl">
/// The <c>url=</c> annotation value stamped on this push, or <see langword="null"/> when none was
/// stamped (<c>Station:PublicBaseUrl</c> empty, or an id shape <c>ArtworkUrlResolver</c> does not
/// recognize) — never fabricated.
/// </param>
public sealed record EnginePushResult(string Rid, string? ArtworkUrl);
