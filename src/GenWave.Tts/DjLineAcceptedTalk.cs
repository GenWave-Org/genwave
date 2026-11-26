using GenWave.Core.Domain;
using GenWave.Core.TalkRules;

namespace GenWave.Tts;

/// <summary>
/// The accepted attempt of a rules-mode render, held until the writer has the utterance id: what
/// <see cref="DjLineRulesFlow.AcceptedAsync"/> needs to tell the recording rules and to write the attempt's <c>talk_check</c> row.
/// </summary>
internal sealed record DjLineAcceptedTalk(
    SegmentRequest Request, string Raw, string Cleaned, TalkSnapshot Snapshot, TalkList<RuleVerdict> Verdicts);
