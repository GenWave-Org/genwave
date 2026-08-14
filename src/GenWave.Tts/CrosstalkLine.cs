namespace GenWave.Tts;

/// <summary>
/// One accepted line of a validated <see cref="CrosstalkScript"/> (SPEC F127.3, F127.4, F127.6,
/// STORY-326) — <see cref="Text"/> has already cleared <see cref="LlmCopyWriter.ApplyCopyHygiene"/>
/// and the per-line char budget, and is never trimmed (F127.4: "no line is ever trimmed — a cut
/// dialogue line breaks the reaction to it"). <see cref="IsInterjection"/> (SPEC F127.3, F127.6)
/// marks a line that overlaps the TAIL of the line immediately before it rather than following it in
/// turn order — <see cref="CrosstalkScriptParser"/>'s one sanctioned exception to strict
/// speaker alternation, and (a LATER task, T285's per-line render / T287's assembler) the signal to
/// mix this line's render starting before the previous line's render has finished, rather than after
/// it.
/// </summary>
public sealed record CrosstalkLine(CrosstalkSpeaker Speaker, string Text, bool IsInterjection);
