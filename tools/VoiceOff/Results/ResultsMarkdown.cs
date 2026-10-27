using System.Globalization;
using System.Text;
using GenWave.VoiceOff.Deck;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Serving;

namespace GenWave.VoiceOff.Score;

/// <summary>
/// The text of <c>RESULTS.md</c> (SPEC F217.9). Pure: the same input gives the same string, in invariant culture, with <c>\n</c> line endings and a trailing newline.
/// Notes are not table cells: each goes verbatim inside a code fence longer than any backtick run in it, so a stray <c>|</c>, <c>#</c> or fence cannot break the page.
/// </summary>
internal static class ResultsMarkdown
{
    private const string Absent = "—";

    internal static string Render(ScoreInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        IReadOnlyList<RankedCard> ranking = Ranking.Of(input.Manifest, input.Judgement);
        StringBuilder text = new();
        WriteHeader(text, input.Manifest);
        WriteRanking(text, ranking, input.Cards);
        WriteNotes(text, ranking);
        WriteTray(text, input.Manifest);
        WriteFacts(text, input);
        return text.ToString();
    }

    private static void WriteHeader(StringBuilder text, RunManifest manifest)
    {
        string hosts = string.Join(", ", manifest.Cards.Values.Select(c => c.Host).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Line(text, "# 🎙️ Voice Off results");
        Line(text);
        Line(text, $"- Run: {manifest.RunId}");
        Line(text, $"- Date: {manifest.StartedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        Line(text, $"- Script hash: {manifest.ScriptHash}");
        Line(text, $"- Hosts: {hosts}");
        Line(text);
    }

    private static void WriteRanking(StringBuilder text, IReadOnlyList<RankedCard> ranking, IReadOnlyDictionary<string, Card> cards)
    {
        Line(text, "## 🏆 Ranking");
        Line(text);
        if (ranking.Count == 0)
        {
            Line(text, "No card rendered in this run.");
            Line(text);
            return;
        }

        Line(text, MarkdownCell.Row(["Rank", "Card", "Engine", "Host", "Winces", "Stars", "Why"]));
        Line(text, MarkdownCell.Divider(7));
        foreach (RankedCard row in ranking)
        {
            cards.TryGetValue(row.CardId, out Card? card);
            Line(text, MarkdownCell.Row(
            [
                Number(row.Rank),
                MarkdownCell.Escape(row.CardId),
                MarkdownCell.Escape(card?.Engine ?? Absent),
                MarkdownCell.Escape(row.Entry.Host),
                row.Judged is { } judged ? Number(judged.Winces) : Absent,
                row.Judged?.Stars is int stars ? Number(stars) : Absent,
                MarkdownCell.Escape(card?.Why ?? Absent),
            ]));
        }

        Line(text);
    }

    private static void WriteNotes(StringBuilder text, IReadOnlyList<RankedCard> ranking)
    {
        Line(text, "## 📝 Notes");
        Line(text);
        foreach (RankedCard row in ranking)
        {
            Line(text, $"### {row.CardId}");
            Line(text);
            string note = (row.Judged?.Notes ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            if (note.Length == 0)
            {
                Line(text, "No notes.");
            }
            else
            {
                string fence = new('`', Math.Max(3, LongestBacktickRun(note) + 1));
                Line(text, fence);
                Line(text, note);
                Line(text, fence);
            }

            Line(text);
        }
    }

    private static void WriteTray(StringBuilder text, RunManifest manifest)
    {
        Line(text, "## 🚫 Did not render");
        Line(text);
        List<KeyValuePair<string, CardEntry>> failed = [.. manifest.Cards.Where(c => !c.Value.Rendered).OrderBy(c => c.Key, StringComparer.Ordinal)];
        if (failed.Count == 0)
        {
            Line(text, "Every card rendered.");
            Line(text);
            return;
        }

        Line(text, MarkdownCell.Row(["Card", "Reason"]));
        Line(text, MarkdownCell.Divider(2));
        foreach ((string id, CardEntry entry) in failed)
        {
            Line(text, MarkdownCell.Row([MarkdownCell.Escape(id), MarkdownCell.Escape(entry.Reason ?? "No reason was recorded.")]));
        }

        Line(text);
    }

    private static void WriteFacts(StringBuilder text, ScoreInput input)
    {
        Line(text, "## 📋 Facts");
        Line(text);
        Line(text, MarkdownCell.Row(["Card", "Checkpoint", "Voices", "Cloning", "Own voice", "Cannot", "Tricks", "Variant of", "Render time"]));
        Line(text, MarkdownCell.Divider(9));
        foreach (string id in input.Manifest.Cards.Keys.Order(StringComparer.Ordinal))
        {
            if (!input.Cards.TryGetValue(id, out Card? card))
            {
                Line(text, MarkdownCell.Row([MarkdownCell.Escape(id), .. Enumerable.Repeat(Absent, 7), RenderTime(input.Renders, id)]));
                continue;
            }

            CardFacts facts = card.Facts;
            Line(text, MarkdownCell.Row(
            [
                MarkdownCell.Escape(id),
                MarkdownCell.Escape(facts.Checkpoint),
                facts.Voices.Count is int count ? Number(count) : CardVoices.CloneKeyword,
                YesNo(facts.Cloning),
                YesNo(facts.OwnVoice),
                MarkdownCell.Escape(Joined(facts.Cannot, ", ")),
                MarkdownCell.Escape(Joined(facts.Tricks, "; ")),
                MarkdownCell.Escape(card.VariantOf ?? Absent),
                RenderTime(input.Renders, id),
            ]));
        }
    }

    // The render log's only use: the seconds the engine spent on a card, summed over its paragraphs.
    private static string RenderTime(IReadOnlyList<RenderRecord> renders, string cardId)
    {
        List<RenderRecord> mine = [.. renders.Where(r => string.Equals(r.Card, cardId, StringComparison.Ordinal))];
        return mine.Count == 0 ? Absent : (mine.Sum(r => r.WallMs) / 1000).ToString("F1", CultureInfo.InvariantCulture) + " s";
    }

    private static string Joined(IReadOnlyList<string> items, string separator) =>
        items.Count == 0 ? Absent : string.Join(separator, items);

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static int LongestBacktickRun(string text)
    {
        int longest = 0;
        int run = 0;
        foreach (char c in text)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return longest;
    }

    private static void Line(StringBuilder text, string line = "") => text.Append(line).Append('\n');
}
