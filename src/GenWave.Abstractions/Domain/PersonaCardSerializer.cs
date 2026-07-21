using System.Text.Json;

namespace GenWave.Core.Domain;

/// <summary>
/// The one canonical (de)serialization for <see cref="PersonaCard"/> (SPEC F71.1, STORY-192 AC2):
/// camelCase field names — the card <i>is</i> the future export format (ARCHITECTURE.md), plain JSON
/// any tool can read, not a C#-flavored payload — and every property always written (an empty
/// <see cref="PersonaCard.Quirks"/>/<see cref="PersonaCard.Lore"/>/<see cref="PersonaCard.Corrections"/>
/// serializes as <c>[]</c>, never omitted) so a round trip is byte-stable. Every writer and reader of
/// a <c>persona.definition</c> column MUST go through this — a second, differently-configured
/// <see cref="JsonSerializerOptions"/> instance would silently break the byte-stable guarantee.
/// </summary>
public static class PersonaCardSerializer
{
    /// <summary>
    /// camelCase, unindented, no options that omit or reorder a property — the exact configuration
    /// STORY-192 AC2 pins byte-stability against.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static string Serialize(PersonaCard card) => JsonSerializer.Serialize(card, Options);

    public static PersonaCard? Deserialize(string json) => JsonSerializer.Deserialize<PersonaCard>(json, Options);
}
