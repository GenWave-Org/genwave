using System.Text.RegularExpressions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Builds a <see cref="PersonaCard"/> from the legacy <c>name/backstory/style/voice</c> shape (SPEC
/// F35.1, STORY-118) — the F71.1 reconciliation every <c>station.persona</c> row's <c>definition</c>
/// column is kept in sync through, whether written by <see cref="PersonaRepository"/>'s admin CRUD or
/// by <see cref="PersonaCardMigrator"/>'s one-shot backfill/default bootstrap (STORY-192).
///
/// <see cref="BuildSoul"/> replicates <c>GenWave.Tts.LlmCopyWriter.BuildPersonaSection</c>'s exact
/// text composition (labeled Backstory/Style lines, empty fields skipped) so a card's
/// <see cref="PersonaCard.Soul"/> is — byte for byte — the same persona-section text the pre-F71
/// prompt already injected (SPEC F71.2's "zero prompt change" guarantee; STORY-192 AC4).
/// </summary>
static partial class LegacyPersonaCardMapper
{
    /// <summary>
    /// Builds a card with empty <see cref="PersonaCard.Quirks"/>/<see cref="PersonaCard.Lore"/>/
    /// <see cref="PersonaCard.Corrections"/> and a neutral <see cref="PersonaCard.EnergyDisposition"/>
    /// — the legacy shape never carried any of those, so there is nothing to reconcile them from
    /// (F71.2: "with no quirks/lore populated").
    /// </summary>
    public static PersonaCard BuildCard(string name, string backstory, string style, string voice) =>
        new(
            SchemaVersion: PersonaCard.CurrentSchemaVersion,
            Name: name,
            Tagline: "",
            Soul: BuildSoul(backstory, style),
            Quirks: [],
            Voice: new VoiceSpec(Engine: "", VoiceId: voice, Pace: 1.0, Language: "en"),
            EnergyDisposition: 0,
            Lore: [],
            Corrections: []);

    /// <summary>
    /// Mirrors <c>LlmCopyWriter.BuildPersonaSection</c> verbatim: one labeled line per non-empty
    /// field, empty fields skipped entirely, empty string (not null — <see cref="PersonaCard.Soul"/>
    /// is non-nullable) when both are empty.
    /// </summary>
    public static string BuildSoul(string backstory, string style)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(backstory)) lines.Add($"Backstory: {backstory}");
        if (!string.IsNullOrEmpty(style)) lines.Add($"Style: {style}");
        return lines.Count == 0 ? "" : string.Join('\n', lines);
    }

    /// <summary>
    /// A stable, lowercase-dash slug candidate for <paramref name="name"/> (F71.1's <c>slug</c>
    /// column) — <see cref="PersonaRepository"/>'s own admin-CRUD reconciliation projection. Never
    /// used for the dedicated <c>"default"</c> bootstrap row, which <see cref="PersonaCardMigrator"/>
    /// assigns directly. Two names that collapse to the same slug (e.g. differing only in case or
    /// punctuation) collide on <c>station.persona</c>'s <c>UNIQUE(slug)</c> constraint exactly like a
    /// duplicate <c>name</c> already did — <see cref="PersonaRepository"/>'s existing unique-violation
    /// handling maps both to the same <see cref="PersonaWriteResult.NameConflict"/> outcome.
    /// </summary>
    public static string Slugify(string name)
    {
        var slug = NonAlphaNumeric().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "persona" : slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphaNumeric();
}
