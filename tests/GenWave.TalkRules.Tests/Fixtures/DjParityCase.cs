namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>One DJ-line reply with the inputs both the legacy truth gate and the rules read.</summary>
/// <param name="Copy">The cleaned reply, sound tags still in it.</param>
/// <param name="FactBlock">The raw fact block, blank lines and all; null when no facts are offered.</param>
/// <param name="Track">The current track's title, when there is one.</param>
/// <param name="Crossing">The crossing track's title, which the checks never exempt.</param>
/// <param name="Owner">The owner's message core, when the talk carries one.</param>
internal sealed record DjParityCase(
    string Copy, string? FactBlock = null, string? Track = null, string? Crossing = null, string? Owner = null);
