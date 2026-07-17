namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// One safe-segment authoring request (SPEC F27.1–F27.5, STORY-078). Both shipped triggers — the
/// synchronous <c>POST /api/safe-segments</c> endpoint (P6) and the one-shot boot seed (P7) — build
/// this record and call <see cref="SafeSegmentAuthor.AuthorAsync"/>.
///
/// <see cref="SafeSegmentAuthor"/> cannot read <c>Station:Safe:*</c>, <c>Station:Voice</c>, or
/// <c>Station:Name</c> itself — those bind to <c>StationOptions</c> in <c>GenWave.Host</c>, which
/// depends on this project, not the other way around (the same layering reason
/// <see cref="AudioMixRequest"/> takes <see cref="AudioMixRequest.BedDuckDb"/> /
/// <see cref="AudioMixRequest.BedPadSeconds"/> as fields rather than reading config itself, one layer
/// down). The caller resolves every station-scoped value up front and supplies it here.
/// </summary>
/// <param name="Text">
/// The text to synthesize. Required. May contain the literal token <c>{StationName}</c>, expanded to
/// <see cref="StationName"/> by <see cref="SafeSegmentAuthor.AuthorAsync"/> — the sole expansion point
/// (SPEC F29.1–F29.2, STORY-095); callers pass their raw template through unexpanded.
/// </param>
/// <param name="LibraryId">Destination library for the generated row. Required.</param>
/// <param name="StationName">
/// The station's display name (<c>Station:Name</c>), resolved by the caller. Always becomes the
/// artifact's embedded/tag <c>artist</c> value (F27.2) — nothing in this request can override it.
/// </param>
/// <param name="DefaultVoice">
/// The station's default TTS voice (<c>Station:Voice</c>), resolved by the caller. Used when
/// <see cref="Voice"/> is null (F27.3).
/// </param>
/// <param name="AuthoredRoot">
/// Root directory the generated artifact is written under (<c>Station:Safe:AuthoredRoot</c>),
/// resolved by the caller (F27.1, F11.12).
/// </param>
/// <param name="BedDuckDb">
/// Bed attenuation in dB relative to the voice (<c>Station:Safe:BedDuckDb</c>), resolved by the
/// caller. Ignored when <see cref="Bed"/> is null.
/// </param>
/// <param name="BedPadSeconds">
/// Bed lead-in/tail-out padding in seconds (<c>Station:Safe:BedPadSeconds</c>), resolved by the
/// caller. Ignored when <see cref="Bed"/> is null.
/// </param>
/// <param name="Title">Display title for the segment; defaults to "Please Stand By" when null (F27.3).</param>
/// <param name="Voice">TTS voice to use; defaults to <see cref="DefaultVoice"/> when null (F27.3).</param>
/// <param name="Bed">
/// The bed to mix under the voice, or null for a voice-only render. The caller resolves any
/// operator-supplied <c>bedMediaId</c> to a catalog row and builds this — <see cref="SafeSegmentAuthor"/>
/// takes no DB read dependency beyond the authored-insert write seam.
/// </param>
public sealed record SafeSegmentRequest(
    string Text,
    long LibraryId,
    string StationName,
    string DefaultVoice,
    string AuthoredRoot,
    double BedDuckDb,
    double BedPadSeconds,
    string? Title = null,
    string? Voice = null,
    BedSpec? Bed = null);
