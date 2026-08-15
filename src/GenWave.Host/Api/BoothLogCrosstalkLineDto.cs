namespace GenWave.Host.Api;

/// <summary>
/// One line of <see cref="BoothLogCrosstalkScriptDto.Lines"/> (SPEC F127.11, STORY-329, PLAN T287) —
/// mirrors <see cref="GenWave.Core.Domain.CrosstalkAiredLine"/>, the Api layer's own wire type rather
/// than serializing the domain record directly (the same DTO/domain split <see cref="BoothLogPickDto"/>
/// already keeps one seam over). <see cref="Speaker"/> is the enum's own token name ("Host"/"Neighbor"),
/// stringified for the wire the same way <c>BoothLogEntryDto.SegmentKind</c> already stringifies
/// <c>SegmentKind</c>.
///
/// <para>
/// <b>Security (gh-#346, round-2 review F3 rider):</b> <see cref="Text"/> is untrusted model output
/// rendering into a CSP-less admin surface — any future admin-UI consumer of this field MUST render it
/// as plain text (a React child), never via <c>dangerouslySetInnerHTML</c> or an equivalent raw-HTML
/// sink. This API layer does not render anything itself; the constraint is recorded here for whichever
/// admin-UI change eventually displays it.
/// </para>
/// </summary>
public sealed record BoothLogCrosstalkLineDto(string Speaker, string Text, bool IsInterjection);
