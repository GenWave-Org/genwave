using System.Globalization;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using GenWave.Core.Domain;

namespace GenWave.Host.Configuration;

/// <summary>
/// Validates a proposed string value for an allowlisted configuration key.
///
/// Per-key checks enforce both parseability (matching the CLR type the options binder expects) and
/// numeric range (matching the <c>[Range]</c> attributes on the owning options properties).  This
/// keeps the runtime API guard in sync with the boot-time <c>ValidateDataAnnotations()</c> check so
/// that the same bounds are enforced in both places without duplicating literal numbers.
///
/// Cross-field checks (e.g. GW_XFADE_MIN ≤ GW_XFADE_MAX) are handled by
/// <see cref="ValidateBatch"/>, which has visibility into the full set of proposed values and the
/// current effective configuration.
///
/// Registered as a singleton; thread-safe (stateless beyond the injected <see cref="IConfiguration"/>).
/// </summary>
public sealed class SettingValidator(IConfiguration configuration)
{
    // ── Range constants — kept here so SettingValidator and the [Range] annotations on the
    //    options classes reference the SAME numbers.  [Range] attributes cannot reference
    //    non-const expressions, so the options files carry their own literals; these consts
    //    are the single point of truth that both sides are checked against in review.
    //    If you change a bound here, change the matching [Range] on the options property too.

    internal const double TargetLufsMin  = -40.0;   // LoudnessOptions.TargetLufs [Range(-40, 0)]
    internal const double TargetLufsMax  =   0.0;

    internal const double CeilingDbtpMin = -12.0;   // LoudnessOptions.CeilingDbtp [Range(-12, 0)]
    internal const double CeilingDbtpMax =   0.0;   // positive = above digital FS — nonsense

    // GW_XFADE_MIN / GW_XFADE_MAX — Liquidsoap crossfade seconds.
    // No bound options class; rules enforced purely in this validator.
    internal const double XfadeMinValue = 0.0;      // exclusive — both must be > 0
    internal const double XfadeMaxValue = 30.0;     // F53.1 ceiling (inclusive, closes gitea-#221)

    // GW_SAFE_GAP_SECONDS — inter-safe-track silence gap (F29.6/F29.8, STORY-100).
    // No bound options class; rules enforced purely in this validator.
    internal const double SafeGapMinValue = 0.0;    // inclusive — 0 legally disables the gap (F29.6)
    internal const double SafeGapMaxValue = 600.0;  // F53.1 ceiling (inclusive)

    // F44.2/F44.3 allowlist completion (closes gitea-#197) — floors mirror each key's [Range] where one
    // exists (TtsOptions/LlmOptions, both boot-enforced via ValidateDataAnnotations); the remaining
    // four have NO bound options-class validation at all (plain Configure<T>), so this validator is
    // their ONLY floor, exactly the GW_XFADE_*/GW_SAFE_GAP_SECONDS precedent above.
    //
    // F53.1 (closes gitea-#221) pairs every one of these floors with a ceiling — the settings API's only
    // fat-finger guard; boot validation (ValidateDataAnnotations / StationOptionsValidator) is
    // deliberately NOT tightened to match (F53.2), so these Max consts have no [Range] counterpart.
    internal const int RenderBudgetSecondsMin = 1;      // TtsOptions.RenderBudgetSeconds [Range(1, int.MaxValue)]
    internal const int RenderBudgetSecondsMax = 600;
    internal const int BlurbRetentionHoursMin = 1;      // TtsOptions.BlurbRetentionHours [Range(1, int.MaxValue)]
    internal const int BlurbRetentionHoursMax = 8760;   // 1 year
    internal const int MaxCopyCharsMin        = 1;      // LlmOptions.MaxCopyChars [Range(1, int.MaxValue)]
    internal const int MaxCopyCharsMax        = 10000;
    internal const int PlayHistoryCapacityMin = 1;      // no bound options class — a 0-capacity ring has no operator value
    internal const int PlayHistoryCapacityMax = 5000;
    internal const int ScanIntervalSecondsMin = 1;      // no bound options class — mirrors ScanService's own Math.Max(1, …) clamp
    internal const int ScanIntervalSecondsMax = 86400;  // 1 day
    internal const int EnrichmentConcurrencyMin = 1;    // no bound options class — mirrors EnrichmentService's own Math.Max(1, …) clamp
    internal const int EnrichmentConcurrencyMax = 32;

    // Library:Scan:MissThreshold (SPEC F58.3, closes gitea-#223) — ScanOptions.MissThreshold carries a
    // documentation-only [Range(1, 20)] (same "no bound IValidateOptions" shape as the two Library:*
    // keys above); this validator is the actual floor/ceiling enforcement at both boot (via the
    // station.settings overlay, which always routes through this API) and live-edit time.
    internal const int ScanMissThresholdMin = 1;
    internal const int ScanMissThresholdMax = 20;
    internal const double MinSilenceDurationSecMin = 0.0;   // exclusive — a 0s "minimum silence" is not a silence detector
    internal const double MinSilenceDurationSecMax = 60.0;
    internal const double EnergyWindowSecondsMin = 0.0;     // exclusive — a 0s measurement window measures nothing
    internal const double EnergyWindowSecondsMax = 60.0;

    // Llm:TimeoutSeconds — LlmOptions.TimeoutSeconds [Range(1, int.MaxValue)]; F53.1 ceiling below.
    internal const int LlmTimeoutSecondsMin = 1;
    internal const int LlmTimeoutSecondsMax = 300;

    // Rotation/cadence knobs (SPEC F41.6/F42.2) — floor stays 0 (0 legally disables the knob;
    // [Range(0, int.MaxValue)] on the nested options class is documentation-only, StationOptionsValidator
    // is the real boot floor and is NOT tightened per F53.2). F53.1 adds the ceiling below.
    internal const int RotationRecentWindowMax     = 10000;
    internal const int RotationArtistSeparationMax = 100;
    internal const int StationIdEveryNUnitsMax     = 1000;

    // Library:YearLookup:MinScore (SPEC F48.2/F48.5, X5) — YearLookupOptions.MinScore [Range(0, 100)].
    internal const int YearLookupMinScoreMin = 0;
    internal const int YearLookupMinScoreMax = 100;

    // Station:Envelope:EnergyMin/EnergyMax (SPEC F80.1, F81.1, STORY-212) — StationEnvelopeOptions'
    // own [Range(0.0, 1.0)] (documentation-only; StationOptionsValidator is the real boot floor,
    // same "nested class, root ValidateDataAnnotations() doesn't recurse" story as the rotation/
    // cadence knobs above). Min <= Max is a ValidateBatch cross-field check, mirroring GW_XFADE_*.
    internal const double EnvelopeEnergyMin = 0.0;
    internal const double EnvelopeEnergyMax = 1.0;

    // Maps each allowlisted key to a per-key (range + type) validator.
    static readonly Dictionary<string, Func<string, bool>> Validators =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // LoudnessOptions — doubles with range
            ["Loudness:TargetLufs"]   = v => IsDoubleInRange(v, TargetLufsMin,  TargetLufsMax),
            ["Loudness:CeilingDbtp"]  = v => IsDoubleInRange(v, CeilingDbtpMin, CeilingDbtpMax),

            // Station identity (SPEC F44.1/F44.2, closes gitea-#196) — non-blank strings; boot already
            // guards both via [Required]/[MinLength(1)] on the StationOptions TOP-LEVEL properties
            // (root ValidateDataAnnotations() covers these, unlike the nested StationCadenceOptions/
            // StationRotationOptions floors StationOptionsValidator exists for), but the live-edit
            // path needs its own guard here — this is the F23.1-style 400 for the identity keys.
            ["Station:Name"]  = IsNonBlank,
            ["Station:Voice"] = IsNonBlank,

            // StationCadenceOptions — bools
            ["Station:Cadence:LeadInBeforeEachTrack"]      = IsBool,
            ["Station:Cadence:BackAnnounceAfterEachTrack"] = IsBool,

            // StationCadenceOptions — int in [0, 1000] (mirrors [Range(0, int.MaxValue)] floor; F53.1
            // adds the ceiling). 0 disables station IDs entirely (SPEC F42.2, STORY-136).
            ["Station:Cadence:StationIdEveryNUnits"] = v => IsIntInRange(v, 0, StationIdEveryNUnitsMax),

            // Main rotation scope — same shape and constraints as SafeScope.  An empty list
            // equals a silent station; non-empty is enforced here, on the live-edit path
            // (StationOptionsValidator guards only SafeScope at boot — F23.1's 400 is this entry).
            ["Station:Scope:LibraryIds"] = IsNonEmptyPositiveLongArray,

            // Safe-rotation scope — a JSON-encoded array of positive long ids, e.g. "[1,2]" or "[]".
            // Empty is permitted (operator drains to mksafe silence — F4.4 degraded mode);
            // each present element must be a positive integer.
            // Relaxed from IsNonEmptyPositiveLongArray for STORY-068 / F25.2.
            ["Station:SafeScope:LibraryIds"] = IsPositiveLongArrayAllowEmpty,

            // Active DJ persona — 0 (none) or a positive persona id (F35.2). Existence is NOT
            // checked here: a stale id is legal and degrades at read time (ActivePersonaAccessor,
            // F35.5), so this validator only guards the sign/shape, mirroring StationOptionsValidator's
            // boot-time guard for the same field.
            ["Station:Persona:ActiveId"] = IsNonNegativeLong,

            // Rotation knobs (SPEC F41.6) — integers in [0, ceiling]; 0 legally disables either knob
            // (mirrors StationRotationOptions' [Range(0, int.MaxValue)] floor; F53.1 adds the ceiling).
            ["Station:Rotation:RecentWindow"] = v => IsIntInRange(v, 0, RotationRecentWindowMax),
            ["Station:Rotation:ArtistSeparation"] = v => IsIntInRange(v, 0, RotationArtistSeparationMax),

            // Spectator surface (SPEC F62.1, F62.8, STORY-167/170). SpectatorMode is a plain bool
            // kill switch, same shape as the Cadence/YearLookup:Enabled bools above. PublicStreamUrl
            // is legally empty (the about panel hides the player); any non-empty value must either
            // be an absolute http/https URL (mirrors Tts:Endpoint/Llm:Endpoint) or a genuine
            // same-origin root-relative path such as "/stream" (an Icecast mount fronted by the
            // same origin as the api) — see IsSafePublicStreamUrl for the injection/SSRF guards
            // (rejects "//evil.com" protocol-relative, markup/control characters, whitespace).
            ["Station:SpectatorMode"] = IsBool,
            ["Station:PublicStreamUrl"] = IsSafePublicStreamUrl,

            // TTS endpoint (F36.1–F36.2) — there is no "disabled TTS" state, so an absolute
            // http/https URL is required; empty is rejected (mirrors TtsOptions' [Required, Url]).
            ["Tts:Endpoint"] = v => !string.IsNullOrEmpty(v) && IsAbsoluteHttpUri(v),

            // Operator pronunciation corrections (SPEC F68.5, STORY-185) — a JSON array of
            // {from, to} string pairs; empty ("[]" or blank) means no corrections and is legal.
            ["Tts:Corrections"] = IsValidCorrectionsArray,

            // Piper local-fallback engine (SPEC F70.1, STORY-190) — Endpoint mirrors Llm:Endpoint's
            // own shape: empty is the legal disabled state (Piper not deployed, F70.1), any
            // non-empty value must be an absolute http/https URL. Voice is free text, same
            // "no shape to police" story as Llm:Model — it is never sent on the wire
            // (TtsFallbackOptions' own remarks), only compared by an operator against what the
            // compose `piper` sidecar was actually started with.
            ["Tts:Fallback:Endpoint"] = v => string.IsNullOrEmpty(v) || IsAbsoluteHttpUri(v),
            ["Tts:Fallback:Voice"] = AlwaysValid,

            // Per-kind TTS engine override map (SPEC F70.3, STORY-191) — a JSON object whose keys
            // are valid SegmentKind names and whose values are a known engine name; empty/blank is
            // legal ("no per-kind overrides configured", byte-identical to pre-feature routing).
            ["Tts:EngineByKind"] = IsValidEngineByKindMap,

            // LLM endpoint (F34.2, F36.2) — empty is the legal disabled state (blurbs stay
            // templated); any non-empty value must be an absolute http/https URL.
            ["Llm:Endpoint"] = v => string.IsNullOrEmpty(v) || IsAbsoluteHttpUri(v),

            // LLM model name (F36.2) — free text, including empty (an empty model with a configured
            // endpoint is the operator's own misconfiguration to discover via the fallback-to-template
            // WARN, not something this validator can usefully police).
            ["Llm:Model"] = AlwaysValid,

            // LLM completion budget in seconds (F36.2) — floor mirrors LlmOptions'
            // [Range(1, int.MaxValue)]; F53.1 adds the ceiling.
            ["Llm:TimeoutSeconds"] = v => IsIntInRange(v, LlmTimeoutSecondsMin, LlmTimeoutSecondsMax),

            // F44.2 allowlist completion (closes gitea-#197) — six more live keys join the validator.
            // RenderBudgetSeconds/BlurbRetentionHours/MaxCopyChars floors mirror their options' own
            // [Range(1, int.MaxValue)] (boot-enforced via ValidateDataAnnotations); the remaining
            // three have no bound options-class validation, so this is their only floor. F53.1 pairs
            // every one of these floors with a ceiling (settings-API-only, F53.2).
            ["Tts:RenderBudgetSeconds"] = v => IsIntInRange(v, RenderBudgetSecondsMin, RenderBudgetSecondsMax),
            ["Tts:BlurbRetentionHours"] = v => IsIntInRange(v, BlurbRetentionHoursMin, BlurbRetentionHoursMax),
            ["Llm:MaxCopyChars"] = v => IsIntInRange(v, MaxCopyCharsMin, MaxCopyCharsMax),
            ["Admin:PlayHistoryCapacity"] = v => IsIntInRange(v, PlayHistoryCapacityMin, PlayHistoryCapacityMax),
            ["Library:ScanIntervalSeconds"] = v => IsIntInRange(v, ScanIntervalSecondsMin, ScanIntervalSecondsMax),
            ["Library:EnrichmentConcurrency"] = v => IsIntInRange(v, EnrichmentConcurrencyMin, EnrichmentConcurrencyMax),
            ["Library:Scan:MissThreshold"] = v => IsIntInRange(v, ScanMissThresholdMin, ScanMissThresholdMax),

            // MusicBrainz year lookup (SPEC F48.5, X5, closes gitea-#208). Enabled is a plain bool kill
            // switch; Endpoint mirrors Tts:Endpoint's own "must be a non-empty absolute http/https
            // URL" rule (there is no "disabled endpoint" state distinct from Enabled=false); MinScore
            // mirrors YearLookupOptions' own [Range(0, 100)].
            ["Library:YearLookup:Enabled"] = IsBool,
            ["Library:YearLookup:Endpoint"] = v => !string.IsNullOrEmpty(v) && IsAbsoluteHttpUri(v),
            ["Library:YearLookup:MinScore"] = v => IsIntInRange(v, YearLookupMinScoreMin, YearLookupMinScoreMax),

            // Engine crossfade knobs — exclusive-positive floor, F53.1 inclusive ceiling; cross-field
            // (MIN ≤ MAX) is in ValidateBatch.
            ["GW_XFADE_MIN"] = v => IsDoubleAboveAndAtMost(v, XfadeMinValue, XfadeMaxValue),
            ["GW_XFADE_MAX"] = v => IsDoubleAboveAndAtMost(v, XfadeMinValue, XfadeMaxValue),

            // Inter-safe-track silence gap — negative is rejected; 0 is legal (disables the gap,
            // F29.6), so the lower bound is inclusive (unlike the exclusive GW_XFADE_* bound).
            // F53.1 adds the inclusive ceiling.
            ["GW_SAFE_GAP_SECONDS"] = v => IsDoubleInRange(v, SafeGapMinValue, SafeGapMaxValue),

            // F44.3 enrichment-mode keys — a 0s floor makes no sense for either (a "minimum
            // silence" of 0s detects nothing; a 0s energy window measures nothing), so both are
            // exclusive-positive like GW_XFADE_*; F53.1 adds the inclusive ceiling.
            ["Library:CueDetection:MinSilenceDurationSec"] = v => IsDoubleAboveAndAtMost(v, MinSilenceDurationSecMin, MinSilenceDurationSecMax),
            ["Library:Energy:WindowSeconds"] = v => IsDoubleAboveAndAtMost(v, EnergyWindowSecondsMin, EnergyWindowSecondsMax),

            // LLM degradation pin (SPEC F69.3, STORY-188) — exactly the four values
            // DegradationController's parser recognizes; case-insensitive, mirroring that parser.
            ["Llm:DegradationPin"] = IsValidDegradationPin,

            // Station-default segment envelope (SPEC F80.1, F81.1, STORY-212). Genres is a JSON
            // array of non-blank strings; empty ("[]" or blank) is legal — no genre constraint.
            // EnergyMin/EnergyMax are doubles in [0,1]; Min <= Max is checked in ValidateBatch.
            ["Station:Envelope:Genres"] = IsValidGenresArray,
            ["Station:Envelope:EnergyMin"] = v => IsDoubleInRange(v, EnvelopeEnergyMin, EnvelopeEnergyMax),
            ["Station:Envelope:EnergyMax"] = v => IsDoubleInRange(v, EnvelopeEnergyMin, EnvelopeEnergyMax),
        };

    // ── Per-key validation ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="null"/> when <paramref name="value"/> is valid for
    /// <paramref name="key"/>, or a human-readable error message when it is not.
    /// </summary>
    public string? Validate(string key, string value)
    {
        if (!StationSettingsAllowlist.ByKey.ContainsKey(key))
            return $"Key '{key}' is not an operator-editable setting.";

        if (!Validators.TryGetValue(key, out var validate))
            return $"No validator registered for key '{key}' — this is a bug.";

        return validate(value)
            ? null
            : BuildRangeError(key, value);
    }

    // ── Batch cross-field validation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Checks cross-field invariants across the entire set of proposed updates.
    ///
    /// Currently enforces: the effective GW_XFADE_MIN ≤ the effective GW_XFADE_MAX, where
    /// "effective" means the proposed batch value if present, otherwise the current config value.
    ///
    /// Returns <see langword="null"/> when all cross-field constraints pass, or an error message
    /// describing the first violation.
    /// </summary>
    /// <param name="batch">
    /// The proposed updates as a key → value map (OrdinalIgnoreCase).
    /// Must be pre-validated per-key before calling this method.
    /// </param>
    public string? ValidateBatch(IReadOnlyDictionary<string, string> batch)
    {
        // Resolve effective xfade values: batch wins over current config.
        var effectiveMin = ResolveDouble(batch, "GW_XFADE_MIN");
        var effectiveMax = ResolveDouble(batch, "GW_XFADE_MAX");

        if (effectiveMin.HasValue && effectiveMax.HasValue && effectiveMin.Value > effectiveMax.Value)
        {
            return $"GW_XFADE_MIN ({effectiveMin.Value}) must be ≤ GW_XFADE_MAX ({effectiveMax.Value}).";
        }

        // Station-default envelope (SPEC F81.1, STORY-212) — same effective-value cross-field
        // shape as GW_XFADE_MIN/MAX above; mirrors EnergyRange's own construction-time invariant.
        var effectiveEnergyMin = ResolveDouble(batch, "Station:Envelope:EnergyMin");
        var effectiveEnergyMax = ResolveDouble(batch, "Station:Envelope:EnergyMax");

        if (effectiveEnergyMin.HasValue && effectiveEnergyMax.HasValue && effectiveEnergyMin.Value > effectiveEnergyMax.Value)
        {
            return $"Station:Envelope:EnergyMin ({effectiveEnergyMin.Value}) must be ≤ Station:Envelope:EnergyMax ({effectiveEnergyMax.Value}).";
        }

        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the effective double value for a key: batch value takes priority,
    /// then current configuration.  Returns null if the key is absent or unparseable in both.
    /// </summary>
    double? ResolveDouble(IReadOnlyDictionary<string, string> batch, string key)
    {
        if (batch.TryGetValue(key, out var batchVal) &&
            double.TryParse(batchVal, NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            return b;

        var configVal = configuration[key];
        if (configVal is not null &&
            double.TryParse(configVal, NumberStyles.Float, CultureInfo.InvariantCulture, out var c))
            return c;

        return null;
    }

    // Inclusive both bounds (used for the F53.1-ceilinged doubles that already had an inclusive
    // floor, e.g. GW_SAFE_GAP_SECONDS, and for the pre-existing Loudness:* keys).
    static bool IsDoubleInRange(string v, double min, double max) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
        && d >= min && d <= max;

    // Exclusive lower bound, inclusive upper bound (used by GW_XFADE_MIN/MAX and the two
    // enrichment-mode keys — a 0s floor makes no sense for any of them, so the floor stays
    // exclusive; F53.1 adds the inclusive ceiling).
    static bool IsDoubleAboveAndAtMost(string v, double exclusiveLower, double inclusiveUpper) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
        && d > exclusiveLower && d <= inclusiveUpper;

    static bool IsBool(string v) =>
        bool.TryParse(v, out _);

    // Station:Name / Station:Voice (F44.1/F44.2) — a blank value is always invalid; mirrors the
    // boot-time [Required]/[MinLength(1)] guard on the same StationOptions properties.
    static bool IsNonBlank(string v) => !string.IsNullOrWhiteSpace(v);

    // Inclusive both bounds (used for every F53.1-ceilinged int, plus the pre-existing
    // Library:YearLookup:MinScore). min may be 0 (rotation/cadence knobs, where 0 disables the
    // knob) or 1 (everywhere else a "positive int" floor previously stood alone).
    static bool IsIntInRange(string v, int min, int max) =>
        int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max;

    // Llm:Model has no shape to police beyond "is a string" — the per-key Validators dictionary
    // requires a delegate for every allowlisted key, so this documents "no constraint" explicitly
    // rather than omitting the entry (which SettingValidator.Validate would report as a bug).
    static bool AlwaysValid(string v) => true;

    // Llm:DegradationPin (SPEC F69.3) — "auto" (leaves the mode automatic) or a pinned mode name.
    static bool IsValidDegradationPin(string v) =>
        v.Trim().ToLowerInvariant() is "auto" or "normal" or "soft" or "hard";

    /// <summary>
    /// An absolute, well-formed http/https URL (used for <c>Tts:Endpoint</c>/<c>Llm:Endpoint</c>,
    /// F36.1–F36.2). Any subpath the operator includes (e.g. an OpenAI-compatible gateway mounted
    /// under <c>/openai</c>) is preserved by <c>EndpointUri.Combine</c> at call time — this
    /// validator only checks the value parses as absolute http/https, not its path shape.
    /// </summary>
    static bool IsAbsoluteHttpUri(string v) =>
        Uri.TryCreate(v, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Guards <c>Station:PublicStreamUrl</c> (F62.1, F62.8) against both the SSRF/open-redirect
    /// class of bug and markup injection into the future public <c>&lt;audio src&gt;</c>/about
    /// panel:
    ///   • empty is legal (hides the player),
    ///   • '"', '&lt;', '&gt;', '\', control characters, and whitespace are rejected outright —
    ///     <see cref="Uri.TryCreate(string, UriKind, out Uri)"/> happily accepts all of these
    ///     unescaped in both absolute and relative URIs, so it cannot be relied on alone,
    ///   • otherwise the value must either be an absolute http/https URL, or a genuine same-origin
    ///     root-relative path — see <see cref="IsSameOriginRootRelativePath"/> (this is what keeps
    ///     out protocol-relative "//evil.com", which resolves to an EXTERNAL origin, not the api's
    ///     own).
    /// </summary>
    static bool IsSafePublicStreamUrl(string v)
    {
        if (string.IsNullOrEmpty(v)) return true;
        if (HasDisallowedMarkupOrControlCharacters(v)) return false;
        return IsAbsoluteHttpUri(v) || IsSameOriginRootRelativePath(v);
    }

    // '"'/'<'/'>' block markup injection into a future public page; '\' blocks backslash-based
    // browser URL-parsing quirks; control characters and whitespace (incl. plain spaces) have no
    // legitimate place in a URL an operator would type here.
    static bool HasDisallowedMarkupOrControlCharacters(string v) =>
        v.Any(c => c is '"' or '<' or '>' or '\\' || char.IsControl(c) || char.IsWhiteSpace(c));

    // A single leading '/' (never "//" — that's protocol-relative and resolves to whatever host
    // follows, i.e. an EXTERNAL origin, not this api's own) that also parses as a well-formed
    // relative URI. Caller (IsSafePublicStreamUrl) has already screened out unsafe characters.
    static bool IsSameOriginRootRelativePath(string v) =>
        v.StartsWith('/')
        && !v.StartsWith("//", StringComparison.Ordinal)
        && Uri.TryCreate(v, UriKind.Relative, out _);

    // Persona ids are `long` (the C# projection of station.persona's serial id, cast on the way
    // out — mirrors PersonaRepository's own id::bigint cast), so this parses as long, not int.
    static bool IsNonNegativeLong(string v) =>
        long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0;

    /// <summary>
    /// Validates a JSON-encoded array of positive library ids, e.g. <c>"[1,2]"</c>.
    /// Returns <see langword="true"/> when:
    ///   • the value is valid JSON,
    ///   • the root is a non-empty array,
    ///   • every element is a positive integer (mirrors <see cref="StationOptionsValidator"/>).
    /// Used for <c>Station:Scope:LibraryIds</c> (main scope) where empty is always invalid.
    /// </summary>
    static bool IsNonEmptyPositiveLongArray(string v)
    {
        try
        {
            using var doc = JsonDocument.Parse(v);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return false;
            if (root.GetArrayLength() == 0) return false;
            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Number) return false;
                if (!element.TryGetInt64(out var n) || n <= 0) return false;
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates a JSON-encoded array of positive library ids where an empty array is also valid,
    /// e.g. <c>"[]"</c> or <c>"[1,2]"</c>.
    /// Returns <see langword="true"/> when:
    ///   • the value is valid JSON,
    ///   • the root is an array (empty arrays accepted — F4.4 degraded mode),
    ///   • every present element is a positive integer.
    /// Used for <c>Station:SafeScope:LibraryIds</c> (STORY-068 / F25.2).
    /// </summary>
    static bool IsPositiveLongArrayAllowEmpty(string v)
    {
        try
        {
            using var doc = JsonDocument.Parse(v);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return false;
            if (root.GetArrayLength() == 0) return true;
            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Number) return false;
                if (!element.TryGetInt64(out var n) || n <= 0) return false;
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates <c>Tts:Corrections</c> (SPEC F68.5): a JSON array where every element is an object
    /// carrying string <c>from</c>/<c>to</c> properties (case-insensitive property names, mirroring
    /// <c>SpeechCorrection</c>'s own JSON binding in <c>GenWave.Tts.SpeechCorrectionProvider</c>). An
    /// empty array, or a blank value, is legal — "no corrections configured". A blank/whitespace
    /// <c>from</c> on an individual rule is NOT rejected here: <c>SpeechCorrectionSet.Create</c>
    /// already treats it as a no-op rule by design, so this validator only guards JSON shape, not
    /// rule usefulness.
    /// </summary>
    static bool IsValidCorrectionsArray(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return true;

        try
        {
            using var doc = JsonDocument.Parse(v);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return false;

            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) return false;
                if (!HasStringProperty(element, "from")) return false;
                if (!HasStringProperty(element, "to")) return false;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates <c>Station:Envelope:Genres</c> (SPEC F81.1, STORY-212): a JSON array where every
    /// element is a non-blank string. An empty array, or a blank value, is legal — "no genre
    /// constraint" (F81.1's empty-Genres-means-all-genres contract). Case is not normalized here —
    /// matching is the query's job (case-insensitive), this validator only guards JSON shape.
    /// </summary>
    static bool IsValidGenresArray(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return true;

        try
        {
            using var doc = JsonDocument.Parse(v);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return false;

            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String) return false;
                if (string.IsNullOrWhiteSpace(element.GetString())) return false;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates <c>Tts:EngineByKind</c> (SPEC F70.3, STORY-191): a JSON object whose keys are
    /// valid <see cref="SegmentKind"/> names (case-insensitive, mirroring
    /// <c>GenWave.Tts.TtsEngineByKindProvider</c>'s own parse) and whose values name a known engine
    /// — <c>kokoro</c> or <c>piper</c> (also case-insensitive). An empty object, or a blank value,
    /// is legal — "no per-kind overrides configured", identical to pre-feature routing (F70.3's
    /// empty-map contract).
    /// </summary>
    static bool IsValidEngineByKindMap(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return true;

        try
        {
            using var doc = JsonDocument.Parse(v);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            foreach (var property in root.EnumerateObject())
            {
                // Enum.TryParse<SegmentKind> alone accepts numeric strings (e.g. "0" parses to
                // SegmentKind.StationId, its underlying int value) — reject anything that isn't one
                // of the enum's actual NAMES first, mirroring TtsEngineByKindProvider's own guard.
                if (!IsDefinedSegmentKindName(property.Name)) return false;
                if (!Enum.TryParse<SegmentKind>(property.Name, ignoreCase: true, out _)) return false;
                if (property.Value.ValueKind != JsonValueKind.String) return false;

                var engine = property.Value.GetString();
                if (engine is null) return false;
                if (!engine.Equals("kokoro", StringComparison.OrdinalIgnoreCase)
                    && !engine.Equals("piper", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Enum.TryParse<SegmentKind> alone accepts numeric strings ("0" parses to SegmentKind.StationId,
    // its underlying int value) — reject anything that isn't one of the enum's actual NAMES first.
    static bool IsDefinedSegmentKindName(string name) =>
        Enum.GetNames<SegmentKind>().Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));

    static bool HasStringProperty(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.String;
        }

        return false;
    }

    static string BuildRangeError(string key, string value) => key switch
    {
        var k when k.Equals("Station:Name", StringComparison.OrdinalIgnoreCase)
            => $"Value for '{key}' must not be blank.",
        var k when k.Equals("Station:Voice", StringComparison.OrdinalIgnoreCase)
            => $"Value for '{key}' must not be blank.",
        var k when k.Equals("Loudness:TargetLufs",  StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a number in [{TargetLufsMin}, {TargetLufsMax}].",
        var k when k.Equals("Loudness:CeilingDbtp",  StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a number in [{CeilingDbtpMin}, {CeilingDbtpMax}].",
        var k when k.Equals("GW_XFADE_MIN", StringComparison.OrdinalIgnoreCase) ||
                   k.Equals("GW_XFADE_MAX", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be greater than {XfadeMinValue} and at most {XfadeMaxValue}.",
        var k when k.Equals("GW_SAFE_GAP_SECONDS", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a number between {SafeGapMinValue} and {SafeGapMaxValue}, inclusive.",
        var k when k.Equals("Station:Scope:LibraryIds", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a non-empty JSON array of positive integer library ids, e.g. [1] or [1,2].",
        var k when k.Equals("Station:SafeScope:LibraryIds", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a JSON array of positive integer library ids (empty is permitted for degraded-mode; main scope requires non-empty), e.g. [] or [1,2].",
        var k when k.Equals("Station:Persona:ActiveId", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a non-negative integer persona id (0 = none).",
        var k when k.Equals("Station:Rotation:RecentWindow", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between 0 and {RotationRecentWindowMax} (0 disables).",
        var k when k.Equals("Station:Rotation:ArtistSeparation", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between 0 and {RotationArtistSeparationMax} (0 disables).",
        var k when k.Equals("Station:Cadence:StationIdEveryNUnits", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between 0 and {StationIdEveryNUnitsMax} (0 disables).",
        var k when k.Equals("Tts:Endpoint", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a non-empty absolute http/https URL.",
        var k when k.Equals("Tts:Corrections", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a JSON array of {{\"from\":\"...\",\"to\":\"...\"}} objects, e.g. [] or [{{\"from\":\"MacLeod\",\"to\":\"Muh-cloud\"}}].",
        var k when k.Equals("Tts:Fallback:Endpoint", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an absolute http/https URL, or empty to disable the Piper fallback engine.",
        var k when k.Equals("Tts:EngineByKind", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a JSON object mapping speech kind names " +
               "(StationId, LeadIn, BackAnnounce, TimeDate) to \"kokoro\" or \"piper\", e.g. {{}} or " +
               "{{\"StationId\":\"piper\"}}.",
        var k when k.Equals("Llm:Endpoint", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an absolute http/https URL, or empty to disable LLM-authored copy.",
        var k when k.Equals("Llm:TimeoutSeconds", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {LlmTimeoutSecondsMin} and {LlmTimeoutSecondsMax} (seconds).",
        var k when k.Equals("Tts:RenderBudgetSeconds", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {RenderBudgetSecondsMin} and {RenderBudgetSecondsMax} (seconds).",
        var k when k.Equals("Tts:BlurbRetentionHours", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {BlurbRetentionHoursMin} and {BlurbRetentionHoursMax} (hours).",
        var k when k.Equals("Llm:MaxCopyChars", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {MaxCopyCharsMin} and {MaxCopyCharsMax} (characters).",
        var k when k.Equals("Admin:PlayHistoryCapacity", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {PlayHistoryCapacityMin} and {PlayHistoryCapacityMax} (entries).",
        var k when k.Equals("Library:ScanIntervalSeconds", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {ScanIntervalSecondsMin} and {ScanIntervalSecondsMax} (seconds).",
        var k when k.Equals("Library:EnrichmentConcurrency", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {EnrichmentConcurrencyMin} and {EnrichmentConcurrencyMax} (workers).",
        var k when k.Equals("Library:Scan:MissThreshold", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {ScanMissThresholdMin} and {ScanMissThresholdMax} (consecutive misses).",
        var k when k.Equals("Library:YearLookup:Enabled", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a boolean (true/false).",
        var k when k.Equals("Library:YearLookup:Endpoint", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a non-empty absolute http/https URL.",
        var k when k.Equals("Library:YearLookup:MinScore", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer in [{YearLookupMinScoreMin}, {YearLookupMinScoreMax}].",
        var k when k.Equals("Library:CueDetection:MinSilenceDurationSec", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be greater than {MinSilenceDurationSecMin} and at most {MinSilenceDurationSecMax}.",
        var k when k.Equals("Library:Energy:WindowSeconds", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be greater than {EnergyWindowSecondsMin} and at most {EnergyWindowSecondsMax}.",
        var k when k.Equals("Station:PublicStreamUrl", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be empty, an absolute http/https URL, " +
               "or a same-origin root-relative path starting with a single '/' (not '//'); no '\"', '<', '>', '\\', control characters, or whitespace.",
        var k when k.Equals("Llm:DegradationPin", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be one of: auto, normal, soft, hard.",
        var k when k.Equals("Station:Envelope:Genres", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a JSON array of non-blank genre names, e.g. [] or [\"Rock\",\"Jazz\"].",
        var k when k.Equals("Station:Envelope:EnergyMin", StringComparison.OrdinalIgnoreCase) ||
                   k.Equals("Station:Envelope:EnergyMax", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a number in [{EnvelopeEnergyMin}, {EnvelopeEnergyMax}].",
        _ => $"Value '{value}' is not valid for '{key}'.",
    };
}
