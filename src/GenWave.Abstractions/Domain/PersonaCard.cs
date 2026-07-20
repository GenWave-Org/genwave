namespace GenWave.Core.Domain;

/// <summary>
/// A persona's portable DEFINITION (SPEC F71.1; ARCHITECTURE.md "Persona foundation"): the versioned
/// JSON document a station's <c>persona.definition</c> column stores, and the future export/import
/// format (Q4). Deliberately split from a persona's STATE — accrued memory (bits, callbacks, notes)
/// lives in <c>persona_memory</c> instead, Postgres-only and never exported — so this card only ever
/// describes who the DJ <i>is</i>, never what they remember.
///
/// <see cref="SchemaVersion"/> lets the shape evolve without a migration: only fields worth
/// querying/filtering by earn a relational column (<c>slug</c>, <c>enabled</c>); everything else
/// lives in this document (postgres-dba's "JSONB as a first-class document store" convention).
/// Validation IS deserialization (ARCHITECTURE.md) — a card that deserializes through
/// <see cref="PersonaCardSerializer"/> is a valid card; nothing further is asserted at this layer.
///
/// <see cref="Quirks"/> and <see cref="Lore"/> are open, evolving sets — prompt assembly samples 2-3
/// quirks per generation and never concatenates all of them (SPEC F71.3; wiring lands in STORY-193,
/// not here). <see cref="EnergyDisposition"/> is a station-DJ energy knob in the <c>-1..+1</c> range;
/// out-of-range values are a future prompt-assembly concern, not a card-construction one.
/// </summary>
public sealed record PersonaCard(
    int SchemaVersion,
    string Name,
    string Tagline,
    string Soul,
    IReadOnlyList<string> Quirks,
    VoiceSpec Voice,
    double EnergyDisposition,
    IReadOnlyList<string> Lore,
    IReadOnlyList<PersonaCorrection> Corrections);
