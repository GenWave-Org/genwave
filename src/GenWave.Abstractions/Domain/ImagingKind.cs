namespace GenWave.Core.Domain;

/// <summary>
/// The content kind of an authored Station Imaging segment (gh-#149) — the radio-imaging role the
/// segment plays. Stored on <c>library.media.imaging_kind</c> as a snake_case token (see
/// <c>ImagingKindTokens</c>, kept in <c>GenWave.Core</c> — it is an implementation-side mapper, not
/// a member of any published interface); scanned (non-authored) rows carry no kind at all, and
/// authored rows that predate the column read as NULL and display as the <see cref="Liner"/>
/// default.
///
/// SPEC F110.2 (STORY-301, PLAN T231) is the first selection consumer:
/// <c>Abstractions.IMediaCatalog.GetRandomReadyByImagingKindAsync</c> draws a random ready row of a
/// given kind for the top-of-hour ident drain — this enum moved here (from <c>GenWave.Core</c>,
/// where gh-#149 first landed it) SO THAT that method could name it in its own signature:
/// <c>GenWave.Abstractions</c> ships as a zero-dependency MIT package, so any type a published
/// interface's signature references must physically live in this project. The namespace
/// (<c>GenWave.Core.Domain</c>) is unchanged — every existing call site keeps compiling with no
/// edit, the same "relocate, never rename" precedent <see cref="FacetField"/>/<see cref="SegmentKind"/>
/// already set for this project. Every OTHER kind still plays no selection role (F110.4 — only what
/// F110.2 strictly requires changed this cycle).
///
/// <para>
/// <b>This move makes the enum part of the frozen MIT contract surface</b> (semver discipline,
/// <c>GenWave.Abstractions.csproj</c>'s own remarks), not just relocated source: appending a new
/// member (a future kind) is binary-compatible but still semantically visible to every third-party
/// <see cref="Abstractions.IMediaCatalog"/> implementer — a switch/pattern-match written against
/// today's four values silently falls through an unhandled fifth unless it already defaults safely.
/// Renaming or removing an existing member (<see cref="Liner"/>/<see cref="StationId"/>/
/// <see cref="Jingle"/>/<see cref="Promo"/>), or renumbering the underlying values, is an outright
/// breaking change once this ships in a tagged release — the same discipline every other publicly
/// referenced Abstractions type already carries.
/// </para>
///
/// <para>
/// <see cref="Ad"/> (SPEC F158.1, STORY-384, gh-#380 epic) is the fifth token, appended last with
/// the prior four values untouched — the DB CHECK constraint on <c>library.media.imaging_kind</c>
/// widened to admit its <c>ad</c> token in the same change (db/42). SPEC F158.4 will later fence
/// every non-null <c>imaging_kind</c> — <see cref="Ad"/> included — out of music rotation, envelopes,
/// Deep Cuts, requests, and <c>/media/random</c> regardless of scope config; the wiring that actually
/// makes that fence true (<c>PlayablePredicate</c>, PLAN T395) is a later task's, not this enum's own
/// — appending this token today does not by itself change what selects. Once that fence lands, a
/// switch written against the pre-5.6.0 four values that assumed "any imaging kind is exempt from
/// music selection" will already hold for the fifth without needing an update; only a switch that
/// branches by SPECIFIC kind needs a new arm.
/// </para>
/// </summary>
public enum ImagingKind
{
    /// <summary>
    /// A short spoken piece over an optional bed ("We'll be right back — stay tuned"). The default:
    /// every segment authored before kinds existed is, behaviorally, a liner.
    /// </summary>
    Liner,

    /// <summary>Station identification ("You're listening to…").</summary>
    StationId,

    /// <summary>A produced/branded musical piece carrying the station identity.</summary>
    Jingle,

    /// <summary>Promotes a show, event, or station feature.</summary>
    Promo,

    /// <summary>
    /// A pre-rendered ad spot (SPEC F158.1, STORY-384, gh-#380 epic) — vended by
    /// <c>Abstractions.IAdSpotSource</c> rather than selected as music. Stored as the <c>ad</c>
    /// snake_case token (db/42). Will be structurally fenced out of music rotation by SPEC F158.4
    /// like every other imaging kind once that fence's own wiring lands (see this type's own
    /// remarks) — not yet true of this member on its own.
    /// </summary>
    Ad,
}
