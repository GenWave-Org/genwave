namespace GenWave.Tts;

/// <summary>
/// Binds the raw <c>Tts:Corrections</c> configuration leaf (SPEC F68.5) — a JSON-encoded array of
/// <c>{from, to}</c> operator pronunciation corrections, e.g.
/// <c>[{"from":"MacLeod","to":"Muh-cloud"}]</c>. Bound the same way every other <c>Tts:*</c> leaf
/// key is (a flat string under the <see cref="Section"/> section), so a live edit through
/// <c>PUT /api/settings</c> reaches <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>
/// with no api restart — the same mechanism <see cref="TtsOptions.Endpoint"/> already relies on.
///
/// Deliberately a single raw-JSON string rather than a bound <c>IList&lt;SpeechCorrection&gt;</c>:
/// the station-settings overlay only expands a stored JSON array into indexed
/// <c>IConfiguration</c> keys for arrays of scalars (see
/// <c>StationSettingsConfigurationProvider.ExtractArrayItems</c> in <c>GenWave.Host</c>), not
/// arrays of objects. Storing the whole array as one opaque "string"-kind setting keeps this on
/// the config-binding shape the overlay already supports; parsing the JSON into a
/// <see cref="SpeechCorrectionSet"/> is <see cref="SpeechCorrectionProvider"/>'s job, not this
/// class's — this class carries only the flat binding, mirroring <see cref="TtsOptions"/>.
/// </summary>
public sealed class TtsCorrectionsOptions
{
    public const string Section = "Tts";

    /// <summary>
    /// Raw JSON array of <c>{from, to}</c> pairs. Null, empty, or malformed means no operator
    /// corrections apply — <see cref="SpeechCorrectionProvider"/> degrades to
    /// <see cref="SpeechCorrectionSet.Empty"/> rather than throwing, so a typo here never breaks
    /// every subsequent render.
    /// </summary>
    public string? Corrections { get; init; }
}
