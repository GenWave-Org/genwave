namespace GenWave.Tts;

/// <summary>
/// How a rules-mode chat ended: the last attempt's <paramref name="Raw"/> reply and <paramref name="Result"/> (the script to air, or
/// the discard the writer reports), and <paramref name="Accepted"/>, the accepted attempt (null when nothing was accepted), which the
/// writer hands to <see cref="ChatRulesFlow.AcceptedAsync"/>.
/// </summary>
internal sealed record ChatRulesResult(string Raw, CrosstalkWriteResult Result, ChatAcceptedTalk? Accepted);
