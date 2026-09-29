using System.Globalization;

namespace GenWave.Host.Engine;

/// <summary>
/// The result of comparing the engine's live <c>gw_tuning</c> reply against the same effective
/// configuration <c>/internal/engine-config</c> serves (SPEC F213.5–F213.6, STORY-488).
/// <see cref="Compute"/> is pure — no I/O, deterministic on its two string inputs;
/// <see cref="ComputeAsync"/> is the one place on this type that reads through the
/// <see cref="IEngineTuningReader"/> seam, mapping any failure there (unreachable, timed out) to
/// <see cref="Unknown"/> rather than letting it propagate.
/// </summary>
sealed record EngineSettingsVerdict(EngineSettingsState State, IReadOnlyList<string> Differs)
{
    /// <summary>The verdict for "the engine could not be asked, or its answer didn't parse"
    /// (SPEC F213.6, AC9/AC10).</summary>
    public static readonly EngineSettingsVerdict Unknown = new(EngineSettingsState.Unknown, []);

    /// <summary>
    /// Reads <paramref name="reader"/> and computes the verdict against
    /// <paramref name="effectiveConfig"/>. Any exception <paramref name="reader"/> throws — refused
    /// connection, DNS failure, its own read timeout, anything transport-shaped — is caught HERE
    /// and turned into <see cref="Unknown"/> (SPEC F213.6, AC9): "the engine is unreachable" is
    /// exactly the case <see cref="EngineSettingsState.Unknown"/> exists for, so this is the one
    /// place on the engine-settings path that catches a bare <see cref="Exception"/> — everywhere
    /// else the specific type propagates. The caller's OWN cancellation is never swallowed: once
    /// <paramref name="ct"/> itself has fired, the exception rethrows instead of degrading to
    /// <see cref="Unknown"/>.
    /// </summary>
    public static async Task<EngineSettingsVerdict> ComputeAsync(
        IEngineTuningReader reader,
        IReadOnlyDictionary<string, string?> effectiveConfig,
        CancellationToken ct)
    {
        string reply;
        try
        {
            reply = await reader.ReadAsync(ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return Unknown;
        }

        return Compute(effectiveConfig, reply);
    }

    /// <summary>
    /// Parses <paramref name="engineReply"/> (space-separated <c>KEY=value</c> pairs, Liquidsoap
    /// float format — e.g. <c>"GW_XFADE_MIN=2.0 GW_XFADE_MAX=8.0 GW_SAFE_GAP_SECONDS=7.0"</c>) and
    /// compares each of <see cref="EngineTuningKeys.All"/> against <paramref name="effectiveConfig"/>
    /// NUMERICALLY (<see cref="CultureInfo.InvariantCulture"/> — <c>"7"</c> equals <c>"7.0"</c>).
    /// <see cref="Unknown"/> whenever the reply is missing one of the three keys, repeats one, or
    /// carries a value that does not parse as a finite number — and the SAME rule applies to
    /// <paramref name="effectiveConfig"/>, so a bad or absent override can't masquerade as inSync
    /// either.
    /// </summary>
    public static EngineSettingsVerdict Compute(
        IReadOnlyDictionary<string, string?> effectiveConfig, string engineReply)
    {
        var reported = ParseReply(engineReply);
        if (reported is null) return Unknown;

        var differs = new List<string>();
        foreach (var key in EngineTuningKeys.All)
        {
            if (!effectiveConfig.TryGetValue(key, out var effectiveRaw) ||
                !TryParseNumber(effectiveRaw, out var effectiveValue))
                return Unknown;

            // reported is guaranteed (by ParseReply's own check below) to carry every key here.
            // Liquidsoap prints floats to 12 significant digits (verified: 2.1234567890123 prints
            // as 2.12345678901), but the settings validator accepts any precision and the UI uses
            // step="any" — an exact == here would read a saved GW_XFADE_MIN=2.1234567890123 as
            // restartNeeded forever, even right after a restart. Rounding both sides to the
            // engine's own print precision before comparing fixes that without hiding a genuine
            // difference at the 12th significant digit.
            if (RoundToEnginePrintPrecision(effectiveValue) != RoundToEnginePrintPrecision(reported[key]))
                differs.Add(key);
        }

        return differs.Count == 0
            ? new EngineSettingsVerdict(EngineSettingsState.InSync, [])
            : new EngineSettingsVerdict(EngineSettingsState.RestartNeeded, differs);
    }

    /// <summary>Parses the reply into key→value, or null when any of
    /// <see cref="EngineTuningKeys.All"/> is missing, duplicated, or not a finite number.</summary>
    static Dictionary<string, double>? ParseReply(string engineReply)
    {
        var reported = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var pair in engineReply.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0 || !TryParseNumber(pair[(eq + 1)..], out var value)) return null;

            var key = pair[..eq];
            if (!reported.TryAdd(key, value)) return null;   // a repeated key never has one truth
        }

        foreach (var key in EngineTuningKeys.All)
            if (!reported.ContainsKey(key)) return null;      // missing one of the three keys

        return reported;
    }

    /// <summary>Rounds to Liquidsoap's own 12-significant-digit print precision, so a value with
    /// more precision than the engine can echo back compares equal to what it printed.</summary>
    static double RoundToEnginePrintPrecision(double value) =>
        double.Parse(value.ToString("G12", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    static bool TryParseNumber(string? raw, out double value)
    {
        value = default;
        return !string.IsNullOrWhiteSpace(raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }
}
