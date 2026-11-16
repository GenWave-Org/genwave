using System.Text;
using GenWave.Core.Domain;

namespace GenWave.Memory;

/// <summary>The lore-suggestion prompt (SPEC F218.20): ask for exactly five one-line entries, one per line, and nothing else.</summary>
public static class LoreDraftPrompt
{
    /// <summary>Most existing lore lines shown to the model, as the admin list orders them, newest first, so a long list cannot overflow its context.</summary>
    public const int MaxExistingLore = 20;

    public static readonly string System =
        $"""
        You write short backstory facts for a radio DJ character, to be spoken on air now and then.
        Reply with exactly {LoreReplyParser.MaxLines} lines, one fact per line, and nothing else: no numbering, no bullets, no quotes, no headings.
        Each fact is one plain sentence of at most {MemoryText.MaxChars} characters about the DJ's past, home, family, habits or odd experiences.
        Stay consistent with the character notes. Never repeat a fact already listed. Never mention music tracks or the weather.
        """;

    /// <summary>The user turn: the character notes and the lore the DJ already has.</summary>
    public static string User(string soul, string backstory, IReadOnlyList<string> existingLore)
    {
        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(soul))
            text.Append("Character:\n").Append(soul.Trim()).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(backstory))
            text.Append("Backstory:\n").Append(backstory.Trim()).Append("\n\n");
        if (existingLore.Count > 0)
            text.Append("Facts already known (do not repeat):\n")
                .AppendJoin('\n', existingLore.Take(MaxExistingLore).Select(l => "- " + l)).Append("\n\n");
        return text.Append($"Write {LoreReplyParser.MaxLines} new facts.").ToString();
    }
}
