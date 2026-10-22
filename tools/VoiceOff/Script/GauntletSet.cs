using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GenWave.Core.Domain;
using GenWave.Tts;

namespace GenWave.VoiceOff.Script;

/// <summary>
/// The Voice Off script set, loaded from a <c>gauntlet</c> directory (SPEC F214.2). Lines come from
/// every <c>*.json</c> file except <c>corrections.json</c> and <c>pronunciations.json</c>, which carry the
/// station's shipped speech rules. <see cref="ContentHash"/> covers all of them, so two runs compare only
/// when it matches.
/// </summary>
internal sealed class GauntletSet
{
    internal const string CorrectionsFile = "corrections.json";
    internal const string PronunciationsFile = "pronunciations.json";
    private const string PendingProvenance = "pending";

    private GauntletSet(
        IReadOnlyList<GauntletLine> lines,
        string contentHash,
        SpeechCorrectionSet corrections,
        PronunciationRuleSet pronunciations,
        bool correctionsPending)
    {
        Lines = lines;
        ContentHash = contentHash;
        Corrections = corrections;
        Pronunciations = pronunciations;
        CorrectionsPending = correctionsPending;
    }

    internal IReadOnlyList<GauntletLine> Lines { get; }

    /// <summary>SHA-256 hex over the canonical JSON of every file in the directory, in ordinal file-name order.</summary>
    internal string ContentHash { get; }

    internal SpeechCorrectionSet Corrections { get; }

    internal PronunciationRuleSet Pronunciations { get; }

    /// <summary>True when a rules file still carries <c>"provenance":"pending"</c> instead of the station's real rules.</summary>
    internal bool CorrectionsPending { get; }

    /// <exception cref="InvalidDataException">A file is malformed or a line breaks a gauntlet rule.</exception>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    internal static GauntletSet Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Gauntlet directory not found: {directory}");
        }

        string[] files = Directory.GetFiles(directory, "*.json")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();
        RequireFile(files, CorrectionsFile);
        RequireFile(files, PronunciationsFile);
        StringBuilder canonical = new();
        List<GauntletLine> lines = [];
        SpeechCorrectionSet corrections = SpeechCorrectionSet.Create([]);
        PronunciationRuleSet pronunciations = PronunciationRuleSet.Create([]);
        bool pending = false;

        foreach (string path in files)
        {
            string name = Path.GetFileName(path);
            JsonNode node = GauntletJson.Parse(File.ReadAllText(path), name);
            canonical.Append(name).Append('\n').Append(CanonicalJson.Of(node)).Append('\n');
            switch (name)
            {
                case CorrectionsFile:
                    (string correctionsProvenance, corrections) = GauntletRulesParser.ParseCorrections(node, name);
                    pending |= IsPending(correctionsProvenance);
                    break;
                case PronunciationsFile:
                    (string pronunciationsProvenance, pronunciations) = GauntletRulesParser.ParsePronunciations(node, name);
                    pending |= IsPending(pronunciationsProvenance);
                    break;
                default:
                    lines.AddRange(GauntletLineParser.Parse(node, name));
                    break;
            }
        }

        if (lines.Count == 0)
        {
            throw new InvalidDataException($"Gauntlet directory {directory} has no lines file.");
        }

        RejectDuplicateIds(lines);
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
        return new GauntletSet(lines, hash, corrections, pronunciations, pending);
    }

    private static void RequireFile(string[] files, string fileName)
    {
        if (!files.Any(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"Gauntlet directory is missing {fileName}.");
        }
    }

    private static bool IsPending(string? provenance) => string.Equals(provenance, PendingProvenance, StringComparison.Ordinal);

    private static void RejectDuplicateIds(List<GauntletLine> lines)
    {
        string? duplicate = lines.GroupBy(l => l.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (duplicate is not null)
        {
            throw new InvalidDataException($"Gauntlet line id {duplicate} appears more than once.");
        }
    }
}
