using GenWave.Core.TalkRules;

namespace GenWave.Tts;

/// <summary>
/// What <see cref="LlmCopyWriter"/> hands <see cref="DjLineRulesFlow"/> for one rules-mode render: the snapshot input
/// the writer already built (attempt 1, no text yet) plus the break state a shaper reads that the input does not carry.
/// </summary>
/// <param name="Input">The snapshot input with its full row lists (the judges see every row, as legacy's check did).</param>
/// <param name="TasteNotes">This break's taste notes, only where legacy prints a taste line; null otherwise.</param>
/// <param name="PreviousTasteNotes">The last on-air break's taste notes; null when there is none to compare.</param>
/// <param name="ExtraLineTaken">A context fact was taken or a crosstalk airs this break.</param>
/// <param name="OffersRecentLines">True where legacy offers recent lines (the persona has a soul or quirks); false hides the rows from the shapers.</param>
internal sealed record DjLineRulesRender(
    DjLineSnapshotInput Input,
    TalkList<string>? TasteNotes,
    TalkList<string>? PreviousTasteNotes,
    bool ExtraLineTaken,
    bool OffersRecentLines);
