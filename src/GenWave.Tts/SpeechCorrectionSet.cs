using System.Text.RegularExpressions;

namespace GenWave.Tts;

/// <summary>
/// Immutable, precompiled collection of operator pronunciation corrections (SPEC F68.5). Each
/// rule's <see cref="SpeechCorrection.From"/> is <see cref="Regex.Escape"/>d before compilation —
/// operator text becomes a literal match, never an arbitrary pattern — with word-boundary anchors
/// added only where the boundary falls on a word-character edge (so a <c>From</c> that starts or
/// ends with a non-word character is not force-anchored there). Matching is case-insensitive and
/// every compiled rule carries a 250ms match timeout so a pathological rule cannot hang the render
/// path; a rule that times out is skipped rather than allowed to fault the whole chokepoint.
/// </summary>
public sealed class SpeechCorrectionSet
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private readonly IReadOnlyList<(Regex Pattern, string To, string From)> rules;

    private SpeechCorrectionSet(IReadOnlyList<(Regex Pattern, string To, string From)> rules)
    {
        this.rules = rules;
    }

    /// <summary>An empty correction set — normalization runs with no operator rules applied.</summary>
    public static SpeechCorrectionSet Empty { get; } = new([]);

    /// <summary>
    /// Compiles operator corrections into an immutable, matchable set. A correction whose
    /// <see cref="SpeechCorrection.From"/> is null, empty, or whitespace-only is skipped rather
    /// than compiled: a blank rule is a no-op by intent, never a corruptor — an unguarded empty
    /// pattern matches at every position in the text and would shred it character by character.
    /// </summary>
    public static SpeechCorrectionSet Create(IEnumerable<SpeechCorrection> corrections)
    {
        ArgumentNullException.ThrowIfNull(corrections);

        var compiled = corrections
            .Where(correction => !string.IsNullOrWhiteSpace(correction.From))
            .Select(correction => (Pattern: CompilePattern(correction.From), correction.To, correction.From))
            .ToList();

        return new SpeechCorrectionSet(compiled);
    }

    /// <summary>
    /// Merges two correction sets: <paramref name="station"/> rules win when a
    /// <paramref name="card"/> rule targets the same <see cref="SpeechCorrection.From"/>
    /// (case-insensitive) — the station-over-card precedence a later persona-card merge needs.
    /// </summary>
    public static SpeechCorrectionSet Merge(SpeechCorrectionSet station, SpeechCorrectionSet card)
    {
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(card);

        var stationFroms = new HashSet<string>(station.rules.Select(rule => rule.From), StringComparer.OrdinalIgnoreCase);
        var merged = new List<(Regex Pattern, string To, string From)>(station.rules);
        merged.AddRange(card.rules.Where(rule => !stationFroms.Contains(rule.From)));

        return new SpeechCorrectionSet(merged);
    }

    /// <summary>
    /// Test-only seam: compiles a single rule from a raw regular expression pattern, bypassing the
    /// <see cref="Regex.Escape"/> step <see cref="Create"/> always applies to operator text. Exists
    /// to exercise the per-rule match timeout deterministically — a pathological pattern cannot be
    /// produced through the escaped, public path. Production code must always go through
    /// <see cref="Create"/>.
    /// </summary>
    internal static SpeechCorrectionSet FromRawPattern(string pattern, string replacement)
    {
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
        return new SpeechCorrectionSet([(regex, replacement, pattern)]);
    }

    /// <summary>
    /// Applies every rule in order, skipping any rule whose match times out. <paramref
    /// name="firedFroms"/> carries every rule's <see cref="SpeechCorrection.From"/> that actually
    /// changed the text, in rule order — empty when nothing fired. This set stays pure (SPEC
    /// F68.7): no logging or counting happens here; <see cref="NormalizingTtsSynthesizer"/> is the
    /// sole reader of <paramref name="firedFroms"/> and does that work itself.
    /// </summary>
    internal string Apply(string text, out IReadOnlyList<string> firedFroms)
    {
        var result = text;
        List<string>? fired = null;

        foreach (var (pattern, to, from) in rules)
        {
            try
            {
                var before = result;
                result = pattern.Replace(result, _ => to);
                if (result != before)
                    (fired ??= []).Add(from);
            }
            catch (RegexMatchTimeoutException)
            {
                // Pathological rule — skip it rather than fault the whole chokepoint (F68.5).
            }
        }

        firedFroms = fired ?? new List<string>();
        return result;
    }

    private static Regex CompilePattern(string from)
    {
        var escaped = Regex.Escape(from);
        var leadingBoundary = StartsWithWordChar(from) ? @"\b" : string.Empty;
        var trailingBoundary = EndsWithWordChar(from) ? @"\b" : string.Empty;
        var pattern = $"{leadingBoundary}{escaped}{trailingBoundary}";

        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
    }

    private static bool StartsWithWordChar(string value) => value.Length > 0 && IsWordChar(value[0]);

    private static bool EndsWithWordChar(string value) => value.Length > 0 && IsWordChar(value[^1]);

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
