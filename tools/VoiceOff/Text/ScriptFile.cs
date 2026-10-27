using System.Text.Json;
using System.Text.Json.Nodes;
using GenWave.VoiceOff.Listening;

namespace GenWave.VoiceOff.Text;

/// <summary>
/// The one script every engine reads (SPEC F217.1), loaded from <c>script/script.json</c>. Read with
/// <see cref="StrictJson"/>: duplicate keys, unknown members, missing members and wrong types are refused.
/// <see cref="Hash"/> moves with any character of the text.
/// </summary>
internal sealed class ScriptFile
{
    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowDuplicateProperties = false };

    private sealed record ParagraphDto(int N, string Text);

    private sealed record ScriptDto(string Version, List<ParagraphDto> Paragraphs);

    private ScriptFile(string version, IReadOnlyList<ScriptParagraph> paragraphs, string hash)
    {
        Version = version;
        Paragraphs = paragraphs;
        Hash = hash;
    }

    internal string Version { get; }

    internal IReadOnlyList<ScriptParagraph> Paragraphs { get; }

    /// <summary>SHA-256 hex over the canonical JSON of the file; whitespace and key order do not move it.</summary>
    internal string Hash { get; }

    /// <summary>The name inside every <c>{...}</c> token in the script, in reading order.</summary>
    internal IReadOnlyList<string> CueTokens() =>
        Paragraphs
            .SelectMany(p => CueVocabulary.Token().Matches(p.Text).Select(m => m.Groups[1].Value))
            .ToList();

    /// <exception cref="InvalidDataException">The file is missing, malformed, or breaks a script rule.</exception>
    internal static ScriptFile Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"Script file not found: {path}");
        }

        return Parse(File.ReadAllText(path), path);
    }

    /// <exception cref="InvalidDataException">The text is not exactly one valid script.</exception>
    internal static ScriptFile Parse(string json, string fileName)
    {
        try
        {
            JsonNode document = JsonNode.Parse(json, documentOptions: DocumentOptions)
                ?? throw new InvalidDataException($"Script file {fileName} is empty.");
            ScriptDto dto = StrictJson.Read<ScriptDto>(json);
            return new ScriptFile(CheckedVersion(dto, fileName), CheckedParagraphs(dto, fileName), ScriptHash.Of(document));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Script file {fileName} is not valid JSON for the script shape: {ex.Message}", ex);
        }
    }

    private static string CheckedVersion(ScriptDto dto, string fileName) =>
        string.IsNullOrWhiteSpace(dto.Version)
            ? throw new InvalidDataException($"Script file {fileName} needs a non-blank version.")
            : dto.Version;

    private static List<ScriptParagraph> CheckedParagraphs(ScriptDto dto, string fileName)
    {
        if (dto.Paragraphs.Count == 0)
        {
            throw new InvalidDataException($"Script file {fileName} needs at least one paragraph.");
        }

        List<ScriptParagraph> paragraphs = [];
        foreach (ParagraphDto paragraph in dto.Paragraphs)
        {
            if (paragraph.N != paragraphs.Count + 1)
            {
                throw new InvalidDataException($"Script file {fileName}: paragraph number {paragraph.N} is out of order; expected {paragraphs.Count + 1}.");
            }

            if (string.IsNullOrWhiteSpace(paragraph.Text))
            {
                throw new InvalidDataException($"Script file {fileName}: paragraph {paragraph.N} has no text.");
            }

            RequireKnownCues(paragraph, fileName);
            paragraphs.Add(new ScriptParagraph(paragraph.N, paragraph.Text));
        }

        return paragraphs;
    }

    // Fails closed: a cue the seven-cue vocabulary lacks would reach an engine as literal braces, or be dropped by one card and not another.
    private static void RequireKnownCues(ParagraphDto paragraph, string fileName)
    {
        string? unknown = CueVocabulary.Token().Matches(paragraph.Text).Select(m => m.Groups[1].Value)
            .FirstOrDefault(name => !CueVocabulary.Script.Contains(name));
        if (unknown is not null)
        {
            throw new InvalidDataException($"Script file {fileName}: paragraph {paragraph.N} uses the cue {{{unknown}}}, which is not one of {string.Join(", ", CueVocabulary.Script.Order(StringComparer.Ordinal))}.");
        }
    }
}
