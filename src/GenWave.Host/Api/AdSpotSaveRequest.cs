using GenWave.Ads;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// The request body shape shared by <c>POST /api/ads</c> (create draft) and
/// <c>PATCH /api/ads/{id}</c> (edit draft/failed) — SPEC F162.1; STORY-390 AC9, STORY-392 AC2; PLAN
/// T403. One type for both verbs (rather than two near-identical records): the two differ only in
/// which fields <see cref="AdsController"/> treats as REQUIRED and how a <see langword="null"/> field
/// is read (POST: "not supplied", refused where required; PATCH: "leave unchanged" — the
/// <c>AdSpotEdit</c>/<c>MediaPatch</c> sparse-update precedent) — a controller-level distinction, not a
/// shape one.
/// </summary>
/// <param name="Brand">The brand this spot advertises.</param>
/// <param name="Title">A short operator-facing label — never read aloud.</param>
/// <param name="Brief">The premise/tone/structure hint this spot's script should be written from —
/// DESCRIPTIVE ONLY (T403 review RULING): never validated, never airable, never itself a substitute
/// for <paramref name="Script"/>. A brief-only draft can be created and edited, but neither
/// <c>POST /api/ads/{id}/approve</c> nor <c>/retry</c> ever airs it — both gate on
/// <see cref="AdSpot.Script"/> being present AND passing the validator (see
/// <see cref="AdsController.ValidateCurrentScriptThenAsync"/>'s own remarks); a still-brief-only spot
/// simply folds into the SAME format-rule refusal a blank script would.</param>
/// <param name="Script">The spot's own line-by-line copy — validated at save (SPEC F160.4) when
/// present.</param>
/// <param name="VoicePlan">The rendered voice cast — <c>[{tag,voiceId,pace}]</c>, reusing
/// <see cref="AdVoicePlanEntry"/> directly (the same wire shape <c>AdRenderService</c> already parses,
/// never a second near-duplicate DTO).</param>
/// <param name="SpotSeconds">One of the three shipped structures — 15, 30, or 60.</param>
/// <param name="BedMediaId">An optional background bed track's <c>library.media</c> id — resolved to a
/// real row before it is ever stored, never trusted as a raw id (the
/// <c>SafeSegmentsController.ResolveBedAsync</c> precedent).</param>
public sealed record AdSpotSaveRequest(
    string? Brand,
    string? Title,
    string? Brief,
    string? Script,
    IReadOnlyList<AdVoicePlanEntry>? VoicePlan,
    int? SpotSeconds,
    long? BedMediaId);
