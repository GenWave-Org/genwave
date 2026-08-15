namespace GenWave.Tts;

/// <summary>
/// A fully validated two-voice banter script (SPEC F127.3, F127.4, STORY-326) — the WHOLE product of
/// one <see cref="CrosstalkScriptWriter"/> completion, never a partial/salvaged one:
/// <see cref="Lines"/> is 3-8 entries long, both <see cref="CrosstalkSpeaker"/> values appear at
/// least once, and strict alternation holds outside any line <see cref="CrosstalkLine.IsInterjection"/>
/// marks — <see cref="CrosstalkScriptParser"/> is the only producer. What <see cref="CrosstalkAssembler"/>
/// (T284) does with this — voicing each line through its own speaker's <c>TtsRenderContext</c> and
/// mixing the renders into one asset — is out of scope here; this type is only the validated
/// intermediate the writer hands off.
/// </summary>
public sealed record CrosstalkScript(IReadOnlyList<CrosstalkLine> Lines);
