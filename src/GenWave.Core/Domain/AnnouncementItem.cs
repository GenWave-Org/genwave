namespace GenWave.Core.Domain;

/// <summary>
/// A claimed owner announcement, carried across the <c>IAnnouncementSource</c> seam into unit
/// assembly (SPEC F144.1, STORY-358). Deliberately narrower than the store's full announcement
/// row: delivery needs only enough to build the on-air segment — nothing about how the row was
/// authored, scheduled, or will later be marked aired travels with it. <see cref="Verbatim"/>
/// selects between the two SPEC F144.2/F144.3 renderings — spoken exactly as written, or given
/// DJ flavor — while <see cref="RequestedVoice"/> carries the operator's own voice pick when one
/// was made, leaving the station's default voice to apply when it wasn't.
/// </summary>
/// <param name="Id">The store row's identity — carried through so a later mark-aired/re-arm
/// transition (owned elsewhere, not this seam) can address the exact row this item came from.</param>
/// <param name="Message">The announcement text as the owner wrote it.</param>
/// <param name="Verbatim">
/// <see langword="true"/> when the message must be spoken exactly as written (SPEC F144.2);
/// <see langword="false"/> when a DJ persona may flavor it in their own words (SPEC F144.3).
/// </param>
/// <param name="RequestedVoice">The owner's requested voice for this announcement, or
/// <see langword="null"/> to use the station's default voice.</param>
public sealed record AnnouncementItem(long Id, string Message, bool Verbatim, string? RequestedVoice);
