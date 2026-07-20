namespace GenWave.Tts;

/// <summary>
/// Binds the raw <c>Tts:EngineByKind</c> configuration leaf (SPEC F70.3, STORY-191) — a
/// JSON-encoded object mapping <see cref="GenWave.Core.Domain.SegmentKind"/> names to an engine
/// name (<c>"kokoro"</c> or <c>"piper"</c>), e.g. <c>{"StationId":"piper","LeadIn":"kokoro"}</c>.
/// Bound the same way every other <c>Tts:*</c> leaf key is (a flat string under
/// <see cref="Section"/>), so a live edit through <c>PUT /api/settings</c> reaches
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/> with no api restart — the same
/// mechanism <see cref="TtsCorrectionsOptions.Corrections"/> already relies on.
///
/// Deliberately a single raw-JSON string, mirroring <see cref="TtsCorrectionsOptions"/>: the
/// station-settings overlay only expands a stored JSON ARRAY into indexed <c>IConfiguration</c>
/// keys for arrays of scalars, and this value is a JSON OBJECT besides — storing it as one opaque
/// "string"-kind setting keeps it on the config-binding shape the overlay already supports.
/// Parsing/validating the JSON into a <see cref="TtsEngineOverrideMap"/> is
/// <see cref="TtsEngineByKindProvider"/>'s job, not this class's.
/// </summary>
public sealed class TtsEngineByKindOptions
{
    public const string Section = "Tts";

    /// <summary>
    /// Raw JSON object mapping SegmentKind names to engine names. Null, empty, or malformed means
    /// no per-kind override applies — every kind falls through to the default (Kokoro-primary,
    /// F70.1) engine routing, byte-identical to pre-feature behavior (F70.3).
    /// </summary>
    public string? EngineByKind { get; init; }
}
