namespace GenWave.Core.Domain;

/// <summary>
/// The sparse fields <c>Abstractions.IAdSpotStore.UpdateAsync</c> may change on a <see cref="AdState.Draft"/>
/// or <see cref="AdState.Failed"/> row (SPEC F162.1; STORY-392; PLAN T403) — the owner editor's own
/// content-edit shape, distinct from the three xmin-guarded STATE transitions
/// (<c>ApproveAsync</c>/<c>RetryAsync</c>/<c>RetireAsync</c>): this never touches <see cref="AdSpot.State"/>.
///
/// <para>
/// <b>Sparse: <see langword="null"/> means "leave this column unchanged," never "clear it"</b> — the
/// <c>MediaPatch</c> precedent (<c>MediaController.Patch</c>'s own doc: "Only non-null fields in the
/// body are written; absent fields are left unchanged"). Accepted trade-off, same as that precedent:
/// there is no way to explicitly null out <see cref="Brief"/>/<see cref="Script"/>/<see cref="VoicePlan"/>/
/// <see cref="BedMediaId"/> back to empty through this shape alone — an operator clearing a field
/// types a replacement instead. At least one field must be non-null for a caller to have anything to
/// write; <c>AdsController</c> enforces that at the HTTP door (400), not this record.
/// </para>
/// </summary>
/// <param name="Brand">The brand this spot advertises, or <see langword="null"/> to leave unchanged.</param>
/// <param name="Title">The operator-facing label, or <see langword="null"/> to leave unchanged.</param>
/// <param name="Brief">The premise/tone/structure hint, or <see langword="null"/> to leave unchanged.</param>
/// <param name="Script">The spot's own line-by-line copy — validated by the caller (SPEC F160.4)
/// BEFORE this call, never re-validated here (the store stays a pure state machine) — or
/// <see langword="null"/> to leave unchanged.</param>
/// <param name="VoicePlan">The voice cast plan, as raw <c>jsonb</c> text, or <see langword="null"/> to
/// leave unchanged.</param>
/// <param name="SpotSeconds">One of the three shipped structures — 15, 30, or 60 — or
/// <see langword="null"/> to leave unchanged.</param>
/// <param name="BedMediaId">An optional background bed track's <c>library.media</c> id, or
/// <see langword="null"/> to leave unchanged.</param>
public sealed record AdSpotEdit(
    string? Brand,
    string? Title,
    string? Brief,
    string? Script,
    string? VoicePlan,
    int? SpotSeconds,
    long? BedMediaId);
