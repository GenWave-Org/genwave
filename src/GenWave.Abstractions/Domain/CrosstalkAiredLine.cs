namespace GenWave.Core.Domain;

/// <summary>
/// One line of a <see cref="CrosstalkAiredScript"/> (SPEC F127.11, STORY-329, PLAN T287) — the SAME
/// shape <c>GenWave.Tts.CrosstalkScriptParser</c> (that project's own validated script-writer producer)
/// emits directly, carried forward unchanged by every consumer. <see cref="Speaker"/> rides
/// <see cref="CrosstalkSpeaker"/> — that enum's own remarks record why it lives in THIS project
/// (GenWave.Abstractions) rather than <c>GenWave.Tts</c>: a second, string-typed mirror of the same
/// two roles (this type's own pre-review-F8 shape) needed a hand-written mapper at every seam that
/// carried a script from render-side to plan-side; casting the SAME enum both sides needs none.
/// </summary>
public sealed record CrosstalkAiredLine(CrosstalkSpeaker Speaker, string Text, bool IsInterjection);
