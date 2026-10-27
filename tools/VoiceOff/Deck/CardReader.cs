using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Text;

namespace GenWave.VoiceOff.Deck;

/// <summary>
/// Reads one card file with <see cref="StrictJson"/> (duplicate keys, unknown or missing members and wrong
/// types are refused), then checks every rule that needs no other card. Failures are
/// <see cref="InvalidDataException"/>s naming the card id (or the file, before the id is known).
/// </summary>
internal static partial class CardReader
{
    private sealed record FactsDto(
        string Checkpoint,
        JsonNode Voices,
        bool Cloning,
        bool OwnVoice,
        List<string> Cannot,
        List<string> Tricks);

    private sealed record CardDto(
        string Id,
        string Engine,
        string Title,
        string Host,
        string Voice,
        double Speed,
        JsonObject Settings,
        Dictionary<string, string?> Cues,
        FactsDto Facts,
        string Why,
        string? VariantOf = null,
        bool? AsShipped = null,
        Dictionary<string, string>? Overrides = null);

    [GeneratedRegex(@"\A(?=.{3,40}\z)[a-z0-9]+(-[a-z0-9]+)*\z")]
    private static partial Regex IdShape();

    /// <summary>Whether <paramref name="id"/> is a card id: strict kebab case, 3 to 40 of a-z and 0-9 in hyphen-separated words.</summary>
    internal static bool IsValidId(string? id) => id is not null && IdShape().IsMatch(id);

    /// <exception cref="InvalidDataException">The text breaks any single-card rule.</exception>
    internal static Card Parse(string json, string fileName)
    {
        CardDto dto = Deserialize(json, fileName);
        string id = dto.Id;
        if (!IdShape().IsMatch(id))
        {
            throw new InvalidDataException($"Card file {fileName}: id \"{id}\" must be kebab case: 3 to 40 of a-z and 0-9 in words joined by single hyphens.");
        }

        Require(dto.Engine, "engine", id);
        Require(dto.Title, "title", id);
        Require(dto.Voice, "voice", id);
        RequireNoControlCharacters(dto.Voice, id);
        if ((object?)dto.Settings is null)
        {
            throw new InvalidDataException($"Card {id}: settings must be an object (use {{}} for none).");
        }

        Require(dto.Why, "why", id);
        Require(dto.Facts.Checkpoint, "facts.checkpoint", id);
        if (dto.AsShipped == false)
        {
            throw new InvalidDataException($"Card {id}: asShipped is only written on the as-shipped card, as true.");
        }

        if (dto.Speed is not (> 0 and <= 4))
        {
            throw new InvalidDataException($"Card {id}: speed must be above 0 and at most 4.");
        }

        if (dto.AsShipped is true && dto.Overrides is { Count: > 0 })
        {
            throw new InvalidDataException($"Card {id}: the as-shipped card takes no overrides; it is the station's own path.");
        }

        return new Card(
            id,
            dto.Engine,
            dto.Title,
            dto.VariantOf,
            dto.AsShipped is true,
            Host(dto.Host, id),
            dto.Voice,
            dto.Speed,
            dto.Settings,
            Cues(dto.Cues, id),
            Overrides(dto.Overrides, id),
            Facts(dto.Facts, id),
            dto.Why);
    }

    private static CardDto Deserialize(string json, string fileName)
    {
        try
        {
            return StrictJson.Read<CardDto>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Card file {fileName} is not valid JSON for the card shape: {ex.Message}", ex);
        }
    }

    private static void Require(string value, string field, string id)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Card {id} needs a non-blank {field}.");
        }
    }

    // The voice name goes into a request body; a line break or other control character is never part of one.
    private static void RequireNoControlCharacters(string voice, string id)
    {
        if (voice.Any(char.IsControl))
        {
            throw new InvalidDataException($"Card {id}: voice must not contain control characters.");
        }
    }

    private static CardHost Host(string host, string id) =>
        host switch
        {
            CardHostName.ThorGpu => CardHost.ThorGpu,
            CardHostName.ThorCpu => CardHost.ThorCpu,
            CardHostName.RunesGpu => CardHost.RunesGpu,
            _ => throw new InvalidDataException($"Card {id}: host \"{host}\" must be thor-gpu, thor-cpu or runes-gpu."),
        };

    private static Dictionary<string, string?> Cues(Dictionary<string, string?> cues, string id)
    {
        foreach ((string key, string? tag) in cues)
        {
            if (!CueVocabulary.Script.Contains(key))
            {
                throw new InvalidDataException($"Card {id}: cues has an unknown cue \"{key}\".");
            }

            if (tag is not null && string.IsNullOrWhiteSpace(tag))
            {
                throw new InvalidDataException($"Card {id}: cue \"{key}\" is blank (use null to strip it).");
            }
        }

        return new Dictionary<string, string?>(cues, StringComparer.Ordinal);
    }

    private static Dictionary<int, string> Overrides(Dictionary<string, string>? overrides, string id)
    {
        Dictionary<int, string> result = [];
        foreach ((string key, string text) in overrides ?? [])
        {
            if (!int.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int paragraph) || paragraph < 1)
            {
                throw new InvalidDataException($"Card {id}: overrides key \"{key}\" must be a 1-based paragraph number.");
            }

            Require(text, $"overrides text for paragraph {paragraph}", id);
            result[paragraph] = text;
        }

        return result;
    }

    private static CardFacts Facts(FactsDto facts, string id) =>
        new(
            facts.Checkpoint,
            Voices(facts.Voices, id),
            facts.Cloning,
            facts.OwnVoice,
            facts.Cannot,
            facts.Tricks);

    // A whole number of built-in voices, or the word "clone"; nothing else.
    private static CardVoices Voices(JsonNode voices, string id)
    {
        if (voices is JsonValue value)
        {
            if (value.TryGetValue(out int count) && count >= 0)
            {
                return new CardVoices(count);
            }

            if (value.TryGetValue(out string? word) && string.Equals(word, CardVoices.CloneKeyword, StringComparison.Ordinal))
            {
                return CardVoices.Cloned;
            }
        }

        throw new InvalidDataException($"Card {id}: facts.voices must be a whole number or \"{CardVoices.CloneKeyword}\".");
    }
}
