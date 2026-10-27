using System.Text.Json.Nodes;
using GenWave.Core.Domain;
using GenWave.Tts;
using PronunciationRule = GenWave.Tts.PronunciationRule;

namespace GenWave.VoiceOff.Text;

/// <summary>Validates the two station-rules files: exactly <c>provenance</c> plus the rule list.</summary>
internal static class GauntletRulesParser
{
    private sealed record CorrectionsDto(string? Provenance, List<SpeechCorrection?>? Corrections);

    private sealed record PronunciationsDto(string? Provenance, List<PronunciationRule?>? Pronunciations);

    internal static (string Provenance, SpeechCorrectionSet Rules) ParseCorrections(JsonNode node, string fileName)
    {
        CorrectionsDto dto = GauntletJson.Read<CorrectionsDto>(node, fileName);
        string provenance = RequireProvenance(dto.Provenance, fileName);
        List<SpeechCorrection?> entries = dto.Corrections
            ?? throw new InvalidDataException($"Gauntlet file {fileName} has no corrections list; use [] for none.");
        List<SpeechCorrection> corrections = [];
        foreach (SpeechCorrection? entry in entries)
        {
            if (entry is null)
            {
                throw new InvalidDataException($"Gauntlet file {fileName} has a null entry in its corrections.");
            }

            if (string.IsNullOrWhiteSpace(entry.From) || entry.To is null)
            {
                throw new InvalidDataException(
                    $"Gauntlet file {fileName} has a correction that needs a non-blank \"from\" and a \"to\" (empty deletes).");
            }

            corrections.Add(entry);
        }

        return (provenance, SpeechCorrectionSet.Create(corrections));
    }

    internal static (string Provenance, PronunciationRuleSet Rules) ParsePronunciations(JsonNode node, string fileName)
    {
        PronunciationsDto dto = GauntletJson.Read<PronunciationsDto>(node, fileName);
        string provenance = RequireProvenance(dto.Provenance, fileName);
        List<PronunciationRule?> entries = dto.Pronunciations
            ?? throw new InvalidDataException($"Gauntlet file {fileName} has no pronunciations list; use [] for none.");
        List<PronunciationRule> rules = [];
        foreach (PronunciationRule? entry in entries)
        {
            if (entry is null)
            {
                throw new InvalidDataException($"Gauntlet file {fileName} has a null entry in its pronunciations.");
            }

            if (string.IsNullOrWhiteSpace(entry.Pattern) || string.IsNullOrWhiteSpace(entry.Word) || string.IsNullOrWhiteSpace(entry.Ipa))
            {
                throw new InvalidDataException(
                    $"Gauntlet file {fileName} has a pronunciation that needs a non-blank \"pattern\", \"word\" and \"ipa\".");
            }

            rules.Add(entry);
        }

        return (provenance, PronunciationRuleSet.Create(rules));
    }

    private static string RequireProvenance(string? provenance, string fileName) =>
        string.IsNullOrWhiteSpace(provenance)
            ? throw new InvalidDataException($"Gauntlet file {fileName} needs a non-blank \"provenance\".")
            : provenance;
}
