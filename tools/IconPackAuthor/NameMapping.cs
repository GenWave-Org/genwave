using System.Text.Json;

namespace GenWave.IconPackAuthor;

/// <summary>
/// Loads the name-mapping file PLAN T305's <c>author</c> subcommand accepts: a flat JSON object,
/// source SVG filename → the icon name it should author under. One entry per glyph a curator wants
/// included in a run — a subset of <c>IconNameContract.Names</c> is entirely legal (SPEC F130.2's "a
/// pack may cover any subset"), and this loader enforces nothing about the NAME's own shape — that
/// question is deferred entirely to
/// <see cref="GenWave.Host.Tests.Support.IconPackAuthoringGateway.Validate"/>, the single source of
/// truth PLAN T305 exists to lean on rather than re-deriving.
/// </summary>
public static class NameMapping
{
    public static IReadOnlyDictionary<string, string> Load(string path)
    {
        if (!File.Exists(path))
            throw new IconPackAuthoringUsageException($"mapping file not found: {path}");

        Dictionary<string, string>? mapping;
        try
        {
            mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new IconPackAuthoringUsageException($"{path}: malformed JSON ({ex.Message})");
        }

        if (mapping is not { Count: > 0 })
            throw new IconPackAuthoringUsageException($"{path}: mapping file declares no entries");

        return mapping;
    }
}
