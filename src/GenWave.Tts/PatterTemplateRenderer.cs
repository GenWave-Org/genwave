namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// Expands a <see cref="SegmentRequest"/> into the spoken copy that will be
/// synthesized by the TTS engine.  Pure string interpolation — no I/O, no
/// external dependencies.
/// </summary>
public sealed class PatterTemplateRenderer
{
    /// <summary>
    /// Returns the patter text for <paramref name="request"/>.
    /// Null-<see cref="SegmentRequest.Track"/> cases produce safe fallback
    /// phrasings — never literal "null", never a <see cref="NullReferenceException"/>.
    ///
    /// <see cref="SegmentKind.LeadIn"/> carries an extra request-color variant (SPEC F87.7, PLAN T91):
    /// when the upcoming track's own <see cref="MediaItem.RequestFulfilled"/> is set (PLAN T90's
    /// carry-through), the generic "got this one in from the request line" acknowledgment leads the
    /// same title/artist phrasing the plain variant already uses — station-known catalog metadata
    /// only, never the wish text or a parsed predicate (neither of which this type — or anything
    /// reaching this renderer — ever carries).
    ///
    /// <see cref="SegmentKind.SignOff"/>/<see cref="SegmentKind.SignOn"/> (SPEC F92.2, F92.5) key off
    /// <see cref="SegmentRequest.CounterpartName"/> instead of <see cref="SegmentRequest.Track"/> —
    /// named when a counterpart exists, music-only phrasing when it doesn't (F92.3). This is the
    /// deterministic fallback rung only; <c>LlmCopyWriter</c> attempts these kinds the same as
    /// LeadIn/BackAnnounce, routing a miss HERE the same way (F12.4) — but unlike LeadIn/BackAnnounce,
    /// a handoff piece that lands on this template rung never actually airs it: <c>TtsSegmentSource</c>
    /// drops any SignOff/SignOn render that isn't genuinely LLM-authored (SPEC F92.4/F92.5 — the ruled
    /// ladder has no "templated piece" rung, only "whichever piece rendered, else clean cut"). This
    /// method still needs a correct, non-throwing arm for both kinds regardless, since
    /// <c>DegradationGatedCopyWriter</c> can route straight here (e.g. Hard mode) before that drop
    /// ever gets a chance to apply.
    /// </summary>
    public string Expand(SegmentRequest request) => request.Kind switch
    {
        SegmentKind.StationId      => $"You're listening to {request.StationName}.",
        SegmentKind.LeadIn         => request.Track switch
                                      {
                                          { RequestFulfilled: true, Artist.Length: > 0 } t =>
                                              $"Got this one in from the request line: {t.Title} by {t.Artist}.",
                                          { RequestFulfilled: true } t =>
                                              $"Got this one in from the request line: {t.Title}.",
                                          { Artist.Length: > 0 } t => $"Coming up: {t.Title} by {t.Artist}.",
                                          { } t                    => $"Coming up: {t.Title}.",
                                          null                     => "Coming up next.",
                                      },
        SegmentKind.BackAnnounce   => request.Track switch
                                      {
                                          { Artist.Length: > 0 } t => $"That was {t.Title} by {t.Artist}.",
                                          { } t                    => $"That was {t.Title}.",
                                          null                     => "That was your last track.",
                                      },
        SegmentKind.TimeDate       => $"It's {request.LocalNow:h:mm tt} here on {request.StationName}.",
        SegmentKind.SignOff        => request.CounterpartName switch
                                      {
                                          { Length: > 0 } name => $"That's me for now — coming up next, {name}.",
                                          _                    => "That's me for now — the music keeps rolling.",
                                      },
        SegmentKind.SignOn         => request.CounterpartName switch
                                      {
                                          { Length: > 0 } name => $"Thanks, {name} — taking it from here.",
                                          _                    => "Taking it from here after a run of nonstop music.",
                                      },
        // placeholder — T224 owns the real context copy
        SegmentKind.ContextSegment => "Here's something worth knowing.",
        _                          => throw new ArgumentOutOfRangeException(
                                        nameof(request.Kind), request.Kind, message: null),
    };
}
