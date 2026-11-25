using GenWave.Core.TalkRules;

namespace GenWave.Tts;

/// <summary>A switched-on rule's verdict that carries a flag, with the flag unwrapped.</summary>
internal sealed record FlaggedRule(RuleVerdict Verdict, RuleFlag Flag);
