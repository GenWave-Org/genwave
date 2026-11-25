using GenWave.Core.TalkRules;

namespace GenWave.Tts;

/// <summary>
/// The accepted attempt of a rules-mode chat, held until the writer has stored the aired lines: what
/// <see cref="ChatRulesFlow.AcceptedAsync"/> needs to tell the recording rules and to write the attempt's <c>talk_check</c> row.
/// </summary>
internal sealed record ChatAcceptedTalk(
    CrosstalkExchangeRequest Request, string Raw, string Cleaned, TalkSnapshot Snapshot, TalkList<RuleVerdict> Verdicts);
