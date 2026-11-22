using System.Text.Json;

namespace GenWave.Core.TalkRules;

/// <summary>
/// What a shaping rule adds before writing: a prompt note and the rule's own memo of its decision.
/// <paramref name="Memo"/> must outlive its source document: build it with <c>SerializeToElement</c> or
/// <c>Clone()</c>, never straight from a <c>JsonDocument</c> that is about to be disposed.
/// </summary>
public sealed record RuleShaping(string? PromptNote, JsonElement? Memo);
