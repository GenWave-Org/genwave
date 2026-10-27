using System.Text.Json.Nodes;

namespace GenWave.VoiceOff.Deck;

/// <summary>One engine setup, read from <c>cards/&lt;cardId&gt;.json</c> (SPEC F217.2).</summary>
internal sealed record Card(
    string Id,
    string Engine,
    string Title,
    string? VariantOf,
    bool AsShipped,
    CardHost Host,
    string Voice,
    double Speed,
    JsonObject Settings,
    IReadOnlyDictionary<string, string?> Cues,
    IReadOnlyDictionary<int, string> Overrides,
    CardFacts Facts,
    string Why);
