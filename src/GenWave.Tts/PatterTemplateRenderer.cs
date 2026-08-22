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
    ///
    /// <see cref="SegmentKind.StationId"/> gains a show-branded variant (SPEC F117.2, STORY-309,
    /// PLAN T250): whenever the Orchestrator's drain arm stamps <see cref="SegmentRequest.ShowName"/>
    /// (only ever done during a show, and only when the authored imaging pool came up empty for it),
    /// the line names the show ahead of the station — "You're listening to {show} on {station}."
    /// A null, empty, or whitespace-only <see cref="SegmentRequest.ShowName"/> (every pre-F117 caller,
    /// and every outside-show drain) renders the ORIGINAL "You're listening to {station}." unchanged —
    /// F117.2's own "outside shows, byte-identical to F110.2" acceptance; a whitespace-only value is
    /// deliberately treated the same as absent (<see cref="string.IsNullOrWhiteSpace(string?)"/>, not
    /// merely a null/empty check) rather than as a real — and visibly broken — spoken show name. No
    /// new <see cref="SegmentKind"/> was
    /// added for this: reusing <see cref="SegmentKind.StationId"/> is what makes zero-LLM routing
    /// (<c>LlmCopyWriter.IsLlmAuthored</c>), station-voicing, and forever-caching all apply for free —
    /// see <c>Orchestrator.BuildStationIdRequest</c>'s own remarks.
    ///
    /// <see cref="SegmentKind.TimeDate"/> (SPEC F110.3, STORY-302, PLAN T232) is ALWAYS this rung —
    /// zero LLM, <c>LlmCopyWriter.IsLlmAuthored</c> does not list it, so there is no rung above this
    /// one to miss. Top-of-hour, o'clock phrasing only (the producer that arms this kind is a
    /// top-of-hour trigger, never a mid-hour one, so minutes never enter into it):
    /// <see cref="SegmentRequest.LocalNow"/>'s hour, mapped 24h→12h (0 and 12 both read "twelve") and
    /// spoken as a word via <see cref="HourWord"/>, never digits — "It's two o'clock," not "It's 2
    /// o'clock." The station name JOINS the line ("…on {station}.") — gh-#453, ruled by Dean
    /// 2026-08-11 after the first live listen ("bare just sounds strange, like we're a time signal"),
    /// overturning T232's original no-station-name cut. Still no minutes and nothing an LLM would
    /// add — the simplest honest phrasing the acceptance criteria (templated, station-voiced,
    /// forever-cacheable) call for. The Orchestrator's own drain arm stamps this field from the
    /// deferral's <c>Due</c> instant (the top of the hour the announcement was ARMED for), never a
    /// fresh drain-time clock read, so the SAME hour always renders the SAME text — the cache-hit half
    /// of F110.3's acceptance.
    ///
    /// <para>
    /// <b>The honest late variant (SPEC F141.2, STORY-355, PLAN T326).</b> When
    /// <see cref="SegmentRequest.TimeDateFreshness"/> reads <see cref="TimeAnnouncementFreshness.Late"/> —
    /// a drain landing more than 90 seconds past the armed hour, still inside the live budget — the
    /// line reads "It's just past {hour} o'clock on {station}." instead: the SAME hour word, the SAME
    /// station name, one honest qualifier. A deferral drained PAST the live budget never reaches this
    /// arm (or any <see cref="SegmentRequest"/>) at all — <c>SpeechDeferralQueue.TryDequeueDue</c>'s own
    /// expiry check drops it first (SPEC F124.4/F141.3), so <see cref="TimeAnnouncementFreshness"/> has
    /// only these two members to switch on (review round-2 finding F3). Per-hour text still means the
    /// forever-cache warms in a day: the late line's own cache key (rendered text) simply gains one more
    /// entry per hour, exactly like the classic one.
    /// </para>
    /// </summary>
    public string Expand(SegmentRequest request) => request.Kind switch
    {
        SegmentKind.StationId      => !string.IsNullOrWhiteSpace(request.ShowName)
                                          ? $"You're listening to {request.ShowName} on {request.StationName}."
                                          : $"You're listening to {request.StationName}.",
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
        SegmentKind.TimeDate       => request.TimeDateFreshness == TimeAnnouncementFreshness.Late
                                          ? $"It's just past {HourWord(request.LocalNow.Hour)} o'clock on {request.StationName}."
                                          : $"It's {HourWord(request.LocalNow.Hour)} o'clock on {request.StationName}.",
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
        // Never actually airs (SPEC F107.6, PLAN T224): TtsSegmentSource drops any ContextSegment
        // render that isn't genuinely LLM-authored — reading raw provider facts as inert filler like
        // this would defeat the whole point of a context provider. This arm exists purely so the
        // switch below stays total: DegradationGatedCopyWriter's unconditional Hard-mode routing needs
        // a correct, non-throwing landing spot for this kind. NOT reached by a persona-preview miss
        // (T224 review finding): ContextSegment is now one of LlmCopyWriter.IsLlmAuthored's kinds, so
        // WritePreviewAsync never falls through to this template rung on an LLM miss the way it would
        // have under T223 — it returns PersonaPreviewResult.Failed instead (F35.6: a preview never
        // silently substitutes template copy). The sibling Crosstalk arm just below carries no such
        // guarantee: TtsSegmentSource's own non-LLM-authored drop guard (`SignOff or SignOn or
        // ContextSegment or Announcement`, TtsSegmentSource.cs, RenderAsync) does not list Crosstalk,
        // so if a future producer (T287) ever routes a Crosstalk render through this template rung,
        // "Two voices, one moment." would become airable filler unless that guard is extended first —
        // the wiring task's call.
        SegmentKind.ContextSegment => "Here's something worth knowing.",
        // No producer builds a Crosstalk SegmentRequest yet (SPEC F127.1, PLAN T281 — the vend
        // itself is T287's), so this arm never reaches air today. It exists purely so the switch
        // below stays total: TemplateCopyWriter's own "never fails for any SegmentRequest" contract,
        // and POST /api/personas/preview (kind is validated against any real SegmentKind name, see
        // PersonaController.TryParseKind), both need a correct, non-throwing landing spot for this
        // kind now that it exists — the same discipline ContextSegment's arm above was added under.
        SegmentKind.Crosstalk      => "Two voices, one moment.",
        // No producer builds an Announcement SegmentRequest yet (SPEC F144.2, PLAN T338 review) —
        // this text is only ever a floor. The real announcement text (the owner's own words) arrives
        // via the dedicated render path a later task wires (T341), never through this template — the
        // same discipline the ContextSegment and Crosstalk arms above already establish: this arm
        // exists purely so the switch below stays total, since Announcement is now a real SegmentKind
        // name that TryParseKind (PersonaController) accepts and TemplateCopyWriter's own "never
        // fails for any SegmentRequest" contract still has to hold for it. Neutral, station-voiced,
        // no fabricated specifics — there is no owner message to read here.
        SegmentKind.Announcement   => "Here's an announcement from the station.",
        _                          => throw new ArgumentOutOfRangeException(
                                        nameof(request.Kind), request.Kind, message: null),
    };

    // Word forms for the 12 possible top-of-hour values (SPEC F110.3) — index 0 is "one" o'clock,
    // index 11 (the wrap) is "twelve". Spoken words, deliberately never digits: a TTS engine reads
    // "2" ambiguously (could land as "two" or spell out "2"), but "two" is unambiguous.
    static readonly string[] HourWords =
    [
        "one", "two", "three", "four", "five", "six",
        "seven", "eight", "nine", "ten", "eleven", "twelve",
    ];

    /// <summary>Maps a 24-hour clock hour (0-23) to its spoken 12-hour word — 0 and 12 both "twelve".</summary>
    static string HourWord(int hour24)
    {
        var hour12 = hour24 % 12;
        return HourWords[hour12 == 0 ? 11 : hour12 - 1];
    }
}
