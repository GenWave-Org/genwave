namespace GenWave.Core.Domain;

/// <summary>
/// The content kind of an authored Station Imaging segment (gh-#149) — the radio-imaging role the
/// segment plays. Stored on <c>library.media.imaging_kind</c> as a snake_case token (see
/// <see cref="ImagingKindTokens"/>); scanned (non-authored) rows carry no kind at all, and authored
/// rows that predate the column read as NULL and display as the <see cref="Liner"/> default.
///
/// METADATA-ONLY FOR NOW: playout and the safe loop treat every kind identically — nothing selects
/// or rotates by kind yet. A future issue wires kind-aware rotation (e.g. drawing station IDs from
/// the <see cref="StationId"/> pool on the F16.2 cadence); this enum only records the fact.
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
}
