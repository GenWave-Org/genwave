using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GenWave.VoiceOff.Script;

namespace GenWave.VoiceOff.Speech;

/// <summary>
/// Reads one engine profile, refusing anything the schema does not allow: duplicate or unknown
/// properties, wrong-case names, JSON null anywhere but a <c>markup</c> value, and every field rule.
/// Every failure is an <see cref="InvalidDataException"/> naming the file (and the field).
/// </summary>
internal static partial class EngineProfileReader
{
    internal const double DefaultSpeed = 1.0;
    internal const int DefaultTimeoutSeconds = 120;
    private const double MaxSpeed = 4.0;
    private const int MaxTimeoutSeconds = 600;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowDuplicateProperties = false };

    private sealed record RepickDto(string? Register, string? Voice, string? Reason);

    private sealed record ProfileDto(
        string? Slug,
        bool? Baseline,
        string? Container,
        int? Port,
        string? Endpoint,
        List<string>? Tiers,
        Dictionary<string, string>? Voices,
        string? VoiceReason,
        RepickDto? Repick,
        Dictionary<string, string?>? Markup,
        double? Speed,
        int? TimeoutSeconds,
        string? License,
        string? Notes);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugShape();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]*$")]
    private static partial Regex ContainerShape();

    /// <exception cref="InvalidDataException">The text breaks any profile rule.</exception>
    internal static EngineProfile Parse(string json, string fileName)
    {
        ProfileDto dto = Deserialize(json, fileName);
        string slug = Slug(dto, fileName);
        return new EngineProfile(
            slug,
            dto.Baseline ?? false,
            Container(dto, fileName),
            Port(dto, fileName),
            Endpoint(dto, fileName),
            Tiers(dto, fileName),
            Voices(dto, fileName),
            Required(dto.VoiceReason, "voiceReason", fileName),
            Repick(dto, fileName),
            Markup(dto, fileName),
            Speed(dto, fileName),
            Timeout(dto, fileName),
            Required(dto.License, "license", fileName),
            dto.Notes);
    }

    private static ProfileDto Deserialize(string json, string fileName)
    {
        try
        {
            JsonNode node = JsonNode.Parse(json, documentOptions: DocumentOptions)
                ?? throw new InvalidDataException($"Engine profile {fileName} is empty.");
            RejectNulls(node, fileName, "$");
            return node.Deserialize<ProfileDto>(Options)
                ?? throw new InvalidDataException($"Engine profile {fileName} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Engine profile {fileName} is not valid JSON for the profile shape: {ex.Message}", ex);
        }
    }

    // Null means "strip this cue" under markup and nothing anywhere else.
    private static void RejectNulls(JsonNode? node, string fileName, string path)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    string child = $"{path}.{property.Key}";
                    if (property.Value is null && path != "$.markup")
                    {
                        throw new InvalidDataException($"Engine profile {fileName} has a null value at {child}; leave the property out instead.");
                    }

                    RejectNulls(property.Value, fileName, child);
                }

                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    if (item is null)
                    {
                        throw new InvalidDataException($"Engine profile {fileName} has a null item in {path}.");
                    }

                    RejectNulls(item, fileName, path + "[]");
                }

                break;
        }
    }

    private static string Required(string? value, string field, string fileName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"Engine profile {fileName} needs a non-blank {field}.")
            : value;

    private static string Slug(ProfileDto dto, string fileName)
    {
        string slug = Required(dto.Slug, "slug", fileName);
        if (!SlugShape().IsMatch(slug))
        {
            throw new InvalidDataException($"Engine profile {fileName}: slug \"{slug}\" must be lowercase words joined by hyphens.");
        }

        string stem = Path.GetFileNameWithoutExtension(fileName);
        return string.Equals(slug, stem, StringComparison.Ordinal)
            ? slug
            : throw new InvalidDataException($"Engine profile {fileName}: slug \"{slug}\" must equal the file name \"{stem}\".");
    }

    private static string Container(ProfileDto dto, string fileName)
    {
        string container = Required(dto.Container, "container", fileName);
        return ContainerShape().IsMatch(container)
            ? container
            : throw new InvalidDataException($"Engine profile {fileName}: container \"{container}\" is not a docker container name.");
    }

    private static int Port(ProfileDto dto, string fileName) =>
        dto.Port is >= 1 and <= 65535 and int port
            ? port
            : throw new InvalidDataException($"Engine profile {fileName} needs a port from 1 to 65535.");

    private static Uri? Endpoint(ProfileDto dto, string fileName)
    {
        if (dto.Endpoint is null)
        {
            return null;
        }

        return Uri.TryCreate(dto.Endpoint, UriKind.Absolute, out Uri? uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : throw new InvalidDataException($"Engine profile {fileName}: endpoint \"{dto.Endpoint}\" must be an absolute http or https URL.");
    }

    private static List<EngineTier> Tiers(ProfileDto dto, string fileName)
    {
        if (dto.Tiers is not { Count: > 0 })
        {
            throw new InvalidDataException($"Engine profile {fileName} needs a non-empty tiers list.");
        }

        List<EngineTier> tiers = [];
        foreach (string name in dto.Tiers)
        {
            EngineTier tier = name switch
            {
                "cpu" => EngineTier.Cpu,
                "gpu" => EngineTier.Gpu,
                _ => throw new InvalidDataException($"Engine profile {fileName}: tier \"{name}\" must be cpu or gpu."),
            };
            if (tiers.Contains(tier))
            {
                throw new InvalidDataException($"Engine profile {fileName}: tier \"{name}\" is listed twice.");
            }

            tiers.Add(tier);
        }

        return tiers;
    }

    private static Dictionary<GauntletRegister, string> Voices(ProfileDto dto, string fileName)
    {
        if (dto.Voices is null)
        {
            throw new InvalidDataException($"Engine profile {fileName} needs voices.");
        }

        foreach (string key in dto.Voices.Keys)
        {
            if (!RegisterKeys.TryParse(key, out _))
            {
                throw new InvalidDataException($"Engine profile {fileName}: voices has an unknown register \"{key}\".");
            }
        }

        Dictionary<GauntletRegister, string> voices = [];
        foreach (GauntletRegister register in RegisterKeys.All)
        {
            string key = RegisterKeys.Key(register);
            if (!dto.Voices.TryGetValue(key, out string? voice) || string.IsNullOrWhiteSpace(voice))
            {
                throw new InvalidDataException($"Engine profile {fileName}: voices needs a non-blank voice for \"{key}\".");
            }

            voices[register] = voice;
        }

        return voices;
    }

    private static EngineRepick? Repick(ProfileDto dto, string fileName)
    {
        if (dto.Repick is null)
        {
            return null;
        }

        if (!RegisterKeys.TryParse(dto.Repick.Register, out GauntletRegister register))
        {
            throw new InvalidDataException($"Engine profile {fileName}: repick.register \"{dto.Repick.Register}\" is not a register.");
        }

        return new EngineRepick(
            register,
            Required(dto.Repick.Voice, "repick.voice", fileName),
            Required(dto.Repick.Reason, "repick.reason", fileName));
    }

    // CueText.Markup strips a cue the map lacks as well as one mapped to null, so a typo'd or missing key
    // would silently drop cues. The map must therefore name every known cue, and only those.
    private static Dictionary<string, string?> Markup(ProfileDto dto, string fileName)
    {
        if (dto.Markup is null)
        {
            throw new InvalidDataException($"Engine profile {fileName} needs a markup map.");
        }

        foreach (string cue in dto.Markup.Keys)
        {
            if (!CueVocabulary.Known.Contains(cue))
            {
                throw new InvalidDataException($"Engine profile {fileName}: markup has an unknown cue \"{cue}\".");
            }
        }

        foreach (string cue in CueVocabulary.Known.Order(StringComparer.Ordinal))
        {
            if (!dto.Markup.TryGetValue(cue, out string? tag))
            {
                throw new InvalidDataException($"Engine profile {fileName}: markup is missing the cue \"{cue}\" (use null to strip it).");
            }

            if (tag is not null && string.IsNullOrWhiteSpace(tag))
            {
                throw new InvalidDataException($"Engine profile {fileName}: markup for \"{cue}\" is blank (use null to strip it).");
            }
        }

        return new Dictionary<string, string?>(dto.Markup, StringComparer.Ordinal);
    }

    private static double Speed(ProfileDto dto, string fileName) =>
        dto.Speed switch
        {
            null => DefaultSpeed,
            > 0 and <= MaxSpeed and double speed => speed,
            _ => throw new InvalidDataException($"Engine profile {fileName}: speed must be above 0 and at most {MaxSpeed}."),
        };

    private static int Timeout(ProfileDto dto, string fileName) =>
        dto.TimeoutSeconds switch
        {
            null => DefaultTimeoutSeconds,
            >= 1 and <= MaxTimeoutSeconds and int seconds => seconds,
            _ => throw new InvalidDataException($"Engine profile {fileName}: timeoutSeconds must be from 1 to {MaxTimeoutSeconds}."),
        };
}
