using GenWave.Core.TalkRules;

namespace GenWave.Tts;

/// <summary>
/// The accepted attempt of a rules-mode ad, held until the writer has recorded the accepted call: what
/// <see cref="IAdRulesFlow.AcceptedAsync"/> needs to tell the recording rules and to write the attempt's <c>talk_check</c> row.
/// </summary>
public sealed record AdRulesAccepted(string Raw, string Cleaned, TalkSnapshot Snapshot, TalkList<RuleVerdict> Verdicts);
