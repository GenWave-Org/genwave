namespace GenWave.Tts;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Live settings subscriber for <c>Tts:Corrections</c> (SPEC F68.5, STORY-185 AC1). Subscribes to
/// <see cref="IOptionsMonitor{TOptions}.OnChange"/> once at construction and rebuilds an immutable
/// <see cref="SpeechCorrectionSet"/> snapshot on every change, so a rule saved through
/// <c>PUT /api/settings</c> reaches the very next render with no api restart.
///
/// <see cref="Current"/> is a plain field read (backed by <see langword="volatile"/>) — every
/// render reads it fresh; nothing here ever hands out a stale snapshot captured at some earlier
/// point in the process lifetime.
///
/// Malformed JSON degrades to <see cref="SpeechCorrectionSet.Empty"/> with one WARN rather than
/// throwing — a typo in the stored corrections must never break every subsequent render (the same
/// degrade-not-throw discipline <see cref="LlmCopyWriter"/> applies to a bad LLM response).
/// Registered as a singleton (<see cref="TtsServiceCollectionExtensions.AddGenWaveTts"/>) so the
/// one subscription lives for the process lifetime.
/// </summary>
public sealed class SpeechCorrectionProvider : IDisposable
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    readonly ILogger<SpeechCorrectionProvider> logger;
    readonly IDisposable? subscription;

    volatile SpeechCorrectionSet current;

    public SpeechCorrectionProvider(
        IOptionsMonitor<TtsCorrectionsOptions> optionsMonitor,
        ILogger<SpeechCorrectionProvider> logger)
    {
        this.logger = logger;
        current = Build(optionsMonitor.CurrentValue, logger);
        subscription = optionsMonitor.OnChange(updated => current = Build(updated, logger));
    }

    /// <summary>The current immutable snapshot of operator corrections.</summary>
    public SpeechCorrectionSet Current => current;

    /// <summary>
    /// The station-over-card merge seam (SPEC F71.7, STORY-193): compiles <paramref
    /// name="cardCorrections"/> the same way <see cref="Current"/>'s own station rules are compiled
    /// (<see cref="SpeechCorrectionSet.Create"/>) and merges them beneath <paramref
    /// name="stationSet"/> via <see cref="SpeechCorrectionSet.Merge"/> — station wins on an
    /// identical <see cref="SpeechCorrection.From"/> (case-insensitive). A free function rather
    /// than an instance method: it needs no state of its own beyond the two sets handed to it, so
    /// callers (<see cref="NormalizingTtsSynthesizer"/>) can build the merged snapshot at their own
    /// render-time cadence without this provider knowing anything about the card side at all.
    /// </summary>
    public static SpeechCorrectionSet BuildMerged(
        SpeechCorrectionSet stationSet, IReadOnlyList<SpeechCorrection> cardCorrections) =>
        SpeechCorrectionSet.Merge(stationSet, SpeechCorrectionSet.Create(cardCorrections));

    static SpeechCorrectionSet Build(TtsCorrectionsOptions options, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.Corrections))
            return SpeechCorrectionSet.Empty;

        try
        {
            var rules = JsonSerializer.Deserialize<List<SpeechCorrection>>(options.Corrections, JsonOptions);
            return rules is null ? SpeechCorrectionSet.Empty : SpeechCorrectionSet.Create(rules);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex, "Tts:Corrections is not valid JSON; no operator corrections applied until it is fixed");
            return SpeechCorrectionSet.Empty;
        }
    }

    public void Dispose() => subscription?.Dispose();
}
