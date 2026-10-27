using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GenWave.VoiceOff.Speech;

/// <summary>
/// Reads one engine profile, refusing anything the schema does not allow: duplicate or unknown
/// properties, wrong-case names, JSON null anywhere, and every field rule.
/// Every failure is an <see cref="InvalidDataException"/> naming the file (and the field).
/// </summary>
internal static partial class EngineProfileReader
{
    internal const int DefaultTimeoutSeconds = 120;
    private const int MaxTimeoutSeconds = 600;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowDuplicateProperties = false };

    private sealed record ProfileDto(
        string? Slug,
        bool? Baseline,
        string? Container,
        int? Port,
        string? Endpoint,
        int? TimeoutSeconds,
        string? License,
        string? Notes);

    [GeneratedRegex(@"\A[a-z0-9]+(-[a-z0-9]+)*\z")]
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

    // Null means nothing anywhere in a profile.
    private static void RejectNulls(JsonNode? node, string fileName, string path)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    string child = $"{path}.{property.Key}";
                    if (property.Value is null)
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

    private static int Timeout(ProfileDto dto, string fileName) =>
        dto.TimeoutSeconds switch
        {
            null => DefaultTimeoutSeconds,
            >= 1 and <= MaxTimeoutSeconds and int seconds => seconds,
            _ => throw new InvalidDataException($"Engine profile {fileName}: timeoutSeconds must be from 1 to {MaxTimeoutSeconds}."),
        };
}
