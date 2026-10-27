namespace GenWave.VoiceOff.Score;

/// <summary>Makes free text safe inside one cell of a markdown table.</summary>
internal static class MarkdownCell
{
    internal static string Escape(string text) =>
        text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    internal static string Row(IEnumerable<string> cells) => "| " + string.Join(" | ", cells) + " |";

    internal static string Divider(int columns) => Row(Enumerable.Repeat("---", columns));
}
