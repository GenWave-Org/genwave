namespace GenWave.Core.TalkRules;

/// <summary>A voice in the talk: persona id, name, and the card phrases (taglines + quirk key phrases).</summary>
public sealed record TalkSpeaker(long PersonaId, string Name, TalkList<string> CardPhrases);
