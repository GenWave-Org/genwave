using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GenWave.VoiceOff.Script;

/// <summary>Turns a lines file's JSON into validated <see cref="GauntletLine"/>s. Every failure names the line id.</summary>
internal static partial class GauntletLineParser
{
    internal const int MinimumWords = 8;

    private sealed record TurnDto(string? Register, string? Text);

    private sealed record LineDto(
        string? Id, string? Class, string? Register, string? Text, List<string?>? Cues, List<TurnDto?>? Turns);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdShape();

    private sealed record FileDto(List<LineDto?>? Lines);

    internal static IEnumerable<GauntletLine> Parse(JsonNode node, string fileName)
    {
        FileDto file = GauntletJson.Read<FileDto>(node, fileName);
        List<LineDto?> lines = file.Lines
            ?? throw new InvalidDataException($"Gauntlet file {fileName} has no lines.");
        if (lines.Count == 0)
        {
            throw new InvalidDataException($"Gauntlet file {fileName} has an empty lines list.");
        }

        return lines.Select(dto => Build(dto, fileName)).ToList();
    }

    private static GauntletLine Build(LineDto? dto, string fileName)
    {
        if (dto is null)
        {
            throw new InvalidDataException($"Gauntlet file {fileName} has a null line.");
        }

        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new InvalidDataException($"A line in {fileName} has no id.");
        }

        string id = dto.Id;
        if (!IdShape().IsMatch(id))
        {
            throw new InvalidDataException($"Line id \"{id}\" in {fileName} must be lowercase words joined by hyphens, like leadin-bubbly-1.");
        }

        GauntletClass lineClass = ParseClass(id, dto.Class);

        return lineClass == GauntletClass.Crosstalk ? BuildCrosstalk(id, dto) : BuildSingle(id, lineClass, dto);
    }

    private static GauntletClass ParseClass(string id, string? value) =>
        value is not null && Enum.GetNames<GauntletClass>().Contains(value, StringComparer.Ordinal)
            ? Enum.Parse<GauntletClass>(value)
            : throw new InvalidDataException($"Line {id} has an unknown class \"{value}\".");

    private static GauntletLine BuildSingle(string id, GauntletClass lineClass, LineDto dto)
    {
        if (dto.Turns is not null)
        {
            throw new InvalidDataException($"Line {id} is not a Crosstalk line, so it takes text, not turns.");
        }

        GauntletRegister register = ParseRegister(id, dto.Register);
        string text = dto.Text is { } given && !string.IsNullOrWhiteSpace(given)
            ? given
            : throw new InvalidDataException($"Line {id} needs non-blank text.");
        CheckWords(id, text);
        string[] cues = CheckCues(id, text, dto.Cues);
        return new GauntletLine(id, lineClass, register, text, cues, []);
    }

    private static GauntletLine BuildCrosstalk(string id, LineDto dto)
    {
        if (dto.Text is not null)
        {
            throw new InvalidDataException($"Line {id} is a Crosstalk line, so it takes turns, not text.");
        }

        if (dto.Register is not null)
        {
            throw new InvalidDataException($"Line {id} is a Crosstalk line, so its voices come from its turns, not a line register.");
        }

        List<GauntletTurn> turns = (dto.Turns
                ?? throw new InvalidDataException($"Line {id} is a Crosstalk line and needs turns."))
            .Select(t => BuildTurn(id, t))
            .ToList();
        if (turns.Count < 2 || turns.Select(t => t.Register).Distinct().Count() < 2)
        {
            throw new InvalidDataException($"Line {id} is a Crosstalk line and needs at least 2 turns in 2 different registers.");
        }

        string joined = string.Join(' ', turns.Select(t => t.Text));
        CheckWords(id, joined);
        string[] cues = CheckCues(id, joined, dto.Cues);
        return new GauntletLine(id, GauntletClass.Crosstalk, null, null, cues, turns);
    }

    private static GauntletTurn BuildTurn(string id, TurnDto? turn)
    {
        if (turn is null)
        {
            throw new InvalidDataException($"Line {id} has a null turn.");
        }

        GauntletRegister register = ParseRegister(id, turn.Register);
        if (string.IsNullOrWhiteSpace(turn.Text) || CountWords(turn.Text) < 1)
        {
            throw new InvalidDataException($"Line {id} has a turn with no words to speak.");
        }

        return new GauntletTurn(register, turn.Text);
    }

    private static GauntletRegister ParseRegister(string id, string? value) =>
        RegisterKeys.TryParse(value, out GauntletRegister register)
            ? register
            : throw new InvalidDataException($"Line {id} has an unknown register \"{value}\".");

    private static void CheckWords(string id, string text)
    {
        int words = CountWords(text);
        if (words < MinimumWords)
        {
            throw new InvalidDataException(
                $"Line {id} has {words} words; every line needs at least {MinimumWords} so loudness measures it reliably.");
        }
    }

    private static int CountWords(string text) =>
        CueVocabulary.Strip(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string[] CheckCues(string id, string text, List<string?>? declared)
    {
        string[] present = CueVocabulary.Token().Matches(text)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (string cue in present.Where(c => !CueVocabulary.Known.Contains(c)))
        {
            throw new InvalidDataException($"Line {id} uses an unknown cue {{{cue}}}.");
        }

        string[] listed = (declared ?? [])
            .Select(c => c ?? throw new InvalidDataException($"Line {id} has a null entry in its cues."))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (listed.Distinct(StringComparer.Ordinal).Count() != listed.Length)
        {
            throw new InvalidDataException($"Line {id} lists a cue more than once.");
        }

        if (!present.SequenceEqual(listed, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Line {id} lists cues [{string.Join(", ", listed)}] but its text holds [{string.Join(", ", present)}].");
        }

        return present;
    }
}
