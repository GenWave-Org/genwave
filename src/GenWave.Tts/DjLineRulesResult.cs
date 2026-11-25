using GenWave.Core.TalkRules;

namespace GenWave.Tts;

/// <summary>
/// How a rules-mode render ended: <paramref name="Cleanup"/> is the cleaned copy of the accepted attempt, or the failure
/// the writer falls back from; <paramref name="Accepted"/> is the snapshot of the accepted attempt (null when nothing was
/// accepted), which the writer hands to <see cref="DjLineRulesFlow.AcceptedAsync"/> with the utterance id.
/// </summary>
internal sealed record DjLineRulesResult(LlmCopyCleanupResult Cleanup, DjLineAcceptedTalk? Accepted);
