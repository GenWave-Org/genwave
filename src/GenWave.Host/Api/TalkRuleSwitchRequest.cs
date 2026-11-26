namespace GenWave.Host.Api;

/// <summary>The PUT body; <c>Enabled</c> is nullable so a body that leaves it out is refused instead of reading as off.</summary>
public sealed record TalkRuleSwitchRequest(bool? Enabled);
