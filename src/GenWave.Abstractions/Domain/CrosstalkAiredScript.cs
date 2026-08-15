namespace GenWave.Core.Domain;

/// <summary>
/// SPEC F127.11 (STORY-329, PLAN T287) — the full validated two-voice script an aired
/// <see cref="SegmentKind.Crosstalk"/> item carries, from generation through to air. Carried on
/// <see cref="MediaItem.CrosstalkScript"/> and <c>GenWave.Core.Events.TrackAired.CrosstalkScript</c>
/// the SAME way <see cref="PersonaPickDiagnostics"/> rides those two records one field over (SPEC
/// F82.6/F86.1) — <c>GenWave.Tts.CrosstalkScriptParser</c> (that project's own validated writer)
/// produces THIS exact shape directly (round-2 review F8: no second, GenWave.Tts-local script/line pair
/// and no mapper between them — see <see cref="CrosstalkAiredLine"/>/<see cref="CrosstalkSpeaker"/>'s
/// own remarks), so this project never needs a reference to <c>GenWave.Tts</c> to carry it forward: a
/// host worker (<c>GenWave.Host.Crosstalk.CrosstalkStockWorker</c>) simply carries the writer's own
/// output onto the stocked exchange it builds, unchanged.
///
/// <para>
/// The booth log's own <c>pick</c> jsonb stamp (<c>BoothLogPickStamp</c>'s own precedent, F86.1)
/// serializes THIS shape for a <see cref="SegmentKind.Crosstalk"/> row instead of a persona-pick
/// stamp — "what did they say" is answerable from the booth log alone, never just the ear (F127.11).
/// </para>
/// </summary>
public sealed record CrosstalkAiredScript(IReadOnlyList<CrosstalkAiredLine> Lines);
