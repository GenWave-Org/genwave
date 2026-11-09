using System.Text.Json.Serialization;

namespace GenWave.Core.Domain;

/// <summary>
/// One structured quirk carried in a <see cref="PersonaCard"/>'s <see cref="PersonaCard.QuirkRules"/>
/// (SPEC F218.5) — additive to the F71.1 card shape; the card stays schema major 1. The flat
/// <see cref="PersonaCard.Quirks"/> list stays authoritative for readers that predate this record,
/// and writers keep the two in lockstep: every <see cref="Text"/> also appears in
/// <see cref="PersonaCard.Quirks"/>.
/// </summary>
/// <param name="Text">The quirk, verbatim as it appears in <see cref="PersonaCard.Quirks"/>.</param>
/// <param name="MaxPerShift">Most times the quirk may fire in one shift; omitted when unlimited. Valid range: at least 1.</param>
/// <param name="PerBreakChance">Chance the quirk fires on any one break; omitted when unconditioned. Valid range: above 0, at most 1.</param>
public sealed record QuirkRule(
    string Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MaxPerShift = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? PerBreakChance = null);
