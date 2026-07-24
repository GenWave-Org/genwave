using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Scriptable <see cref="IRequestCatalogProbe"/> double for STORY-227 (SPEC F87.6, PLAN T90).
/// <see cref="OnGetSelectableById"/>/<see cref="OnFindVibe"/> default to "always null" (the ordinary
/// veto/off-envelope/no-match outcome); a spec overrides either to script the SAME
/// law-vs-envelope-mode distinction the real probe's own two new members encode — most usefully by
/// keying the returned value off whether the received <see cref="SegmentEnvelope"/> argument is
/// <see langword="null"/> (bypass) or not (must satisfy).
/// </summary>
sealed class FakeRequestCatalogProbe : IRequestCatalogProbe
{
    public Func<long, SegmentEnvelope?, MediaReference?> OnGetSelectableById { get; set; } = (_, _) => null;
    public Func<IReadOnlyList<string>, SegmentEnvelope?, MediaReference?> OnFindVibe { get; set; } = (_, _) => null;

    public Task<MediaReference?> GetSelectableByIdAsync(long mediaId, SegmentEnvelope? envelope, CancellationToken ct) =>
        Task.FromResult(OnGetSelectableById(mediaId, envelope));

    public Task<MediaReference?> FindVibeAsync(IReadOnlyList<string> moods, SegmentEnvelope? envelope, CancellationToken ct) =>
        Task.FromResult(OnFindVibe(moods, envelope));

    // Not exercised by STORY-227 specs — T90's fulfillment rung never calls the T89 match query.
    public Task<long?> FindBestAsync(string? artist, string? title, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this fake's own STORY-227 specs.");
}
