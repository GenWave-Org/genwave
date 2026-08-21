using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using GenWave.Core.Domain;
using GenWave.Host.Theming;

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
/// Registered as a singleton; thread-safe (stateless beyond the injected
/// <see cref="IConfiguration"/> and <see cref="ThemeCatalog"/>).
/// </summary>
public sealed partial class SettingValidator
{
    readonly IConfiguration configuration;
    readonly ThemeCatalog themeCatalog;
    readonly Dictionary<string, Func<string, bool>> validators;

    /// <param name="configuration">Read by <see cref="ValidateBatch"/>'s cross-field checks.</param>
    /// <param name="themeCatalog">
    /// The DI-registered runtime theme catalog (shipped ∪ owner, SPEC F103.7, PLAN T182) a proposed
    /// <c>Station:Theme</c> value's slug is checked against (PLAN T183, widening this guard from the
    /// shipped-only set STORY-265 shipped it with). Every production instance gets the real
    /// singleton automatically — it is registered in <c>Program.cs</c> and DI resolves it by type
    /// regardless of constructor-parameter ordering. The default here exists ONLY so the many
    /// existing single-argument <c>new SettingValidator(config)</c> unit tests exercising every
    /// OTHER allowlisted key keep compiling and passing unchanged: with no catalog supplied, this
    /// falls back to <see cref="ThemeCatalog.LoadShipped"/> — the exact shipped-only set this
    /// validator checked <c>Station:Theme</c> against before T183.
    /// </param>
    public SettingValidator(IConfiguration configuration, ThemeCatalog? themeCatalog = null)
    {
        this.configuration = configuration;
        this.themeCatalog = themeCatalog ?? ThemeCatalog.LoadShipped();
        validators = BuildValidators(this.themeCatalog);
    }

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

    // DependencyHealth:* (SPEC F70.2, gh-#125) — floors mirror DependencyHealthOptions' own
    // [Range(1, int.MaxValue)] (boot-enforced via ValidateDataAnnotations); the ceilings are F53.1
    // settings-API-only. The interval ceiling is an hour: anything longer is indistinguishable from
    // "probing is off", and the operator has a real kill switch in the endpoint settings instead.
    // The threshold ceiling of 10 is deliberately tight — threshold × interval is how long a
    // genuinely dead dependency stays undetected, so 10 at the default 30s cadence is already a
    // 5-minute blind spot and further is a footgun, not a tuning option.
    internal const int ProbeIntervalSecondsMin = 1;
    internal const int ProbeIntervalSecondsMax = 3600;
    internal const int ProbeTimeoutSecondsMin = 1;
    internal const int ProbeTimeoutSecondsMax = 300;
    internal const int UnhealthyThresholdMin = 1;
    internal const int UnhealthyThresholdMax = 10;

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

    // Station:Requests:WindowMinutes (SPEC F87.6, STORY-224) — StationRequestsOptions' own
    // [Range(1, int.MaxValue)] floor (documentation-only; StationOptionsValidator is the real boot
    // floor, same nested-class story as the rotation/cadence/envelope knobs above). F53.1 adds the
    // ceiling: 1440 minutes (24h) is generous headroom before "the window never closes" stops
    // meaning anything.
    internal const int RequestsWindowMinutesMin = 1;
    internal const int RequestsWindowMinutesMax = 1440;

    // Context:{Key}:SegmentCadenceMinutes/PatterCadenceMinutes (SPEC F107.2/F107.5, F108.2, PLAN
    // T226) — floors mirror ContextProviderSettings' own contract: no options class exists for a
    // dynamic Context:{Key}:* key (no bound [Range]/IValidateOptions at all), so this validator is
    // the operator-facing write-time floor. History (and any future non-floored provider) uses the
    // generic 1-minute floor below; weather ALONE carries SPEC F108.2's own extra 30-minute floor
    // (F2 fix, T226 review — the write-time range genuinely enforces it, not merely a comment
    // claiming it does) via WeatherSegmentCadenceMinutesMin. Both provider's own
    // GenWave.Context.ICadenceFlooredContextProvider capability, consulted directly by
    // GenWave.Context.ContextPipeline (F4 fix), is the structural backstop for a value that reaches
    // the pipeline some way other than this validator (an appsettings/env override). Both
    // PatterCadenceMinutes floor at 0 — 0 legally means "off" (F107.5). All four share the generic
    // 1440-minute (24h) F53.1 ceiling every other "minutes" knob on this list uses.
    internal const int ContextSegmentCadenceMinutesMin = 1;
    internal const int ContextSegmentCadenceMinutesMax = 1440;
    internal const int WeatherSegmentCadenceMinutesMin = 30;
    internal const int WeatherSegmentCadenceMinutesMax = 1440;
    internal const int ContextPatterCadenceMinutesMin = 0;
    internal const int ContextPatterCadenceMinutesMax = 1440;

    // Station:Shows:PatterCadenceMinutes (SPEC F116.3, STORY-308, PLAN T249) — StationShowsOptions'
    // own documentation-only [Range] (StationOptionsValidator is the real boot floor, the
    // StationCadenceOptions precedent). Floor stays 0 — 0 legally means "off" (F116.3, mirrors
    // ContextPatterCadenceMinutesMin's own "0 = off" floor immediately above); ceiling is the same
    // generic 1440-minute (24h) F53.1 cap every other "minutes" knob on this list uses.
    internal const int ShowsPatterCadenceMinutesMin = 0;
    internal const int ShowsPatterCadenceMinutesMax = 1440;

    // Station:Imaging:TimeAnnouncementBudgetSeconds (SPEC F124.4/F141.1, gh-#469/gh-#526, STORY-321/355,
    // PLAN T269/T326) — StationImagingOptions' own documentation-only [Range] (StationOptionsValidator
    // is the real boot floor, the StationCadenceOptions precedent). Floor is 1, not 0 — unlike the
    // "0 = off" cadence knobs above, 0 has no honest meaning here (a TimeDate is never NOT stale at 0
    // seconds, which would drop every single one undrained — that is not what a live-editable budget
    // is for). Ceiling is 86400s (24h) — the same generic one-day cap the "minutes" knobs elsewhere on
    // this list express as 1440 minutes, translated to this knob's own seconds grain (F141.1).
    internal const int TimeAnnouncementBudgetSecondsMin = 1;
    internal const int TimeAnnouncementBudgetSecondsMax = 86400;

    // Context:{Key}:PersonaId (SPEC F107.7, PLAN T226) — ContextProviderSettings' own remarks: null,
    // 0, and any negative value all mean "the on-air DJ"; only a positive value names an explicit
    // persona. The floor here is a fat-finger guard (F53.1's own ethos), not a domain requirement —
    // a negative value would still resolve safely if it slipped through some other path.
    internal const int ContextPersonaIdMin = 0;

    // Crosstalk:DurationTargetSeconds (SPEC F127.4, STORY-326, PLAN T282) — CrosstalkOptions' own
    // [Range(1, int.MaxValue)] (boot-enforced via ValidateDataAnnotations, the
    // Llm:MaxCopyChars precedent); this validator adds the F53.1 settings-API-only ceiling. Floor of
    // 5s guards a degenerate near-zero target from rejecting every exchange outright; 120s (2
    // minutes) is comfortably past the spec'd 25s default while still bounding a fat-finger entry.
    internal const int CrosstalkDurationTargetSecondsMin = 5;
    internal const int CrosstalkDurationTargetSecondsMax = 120;

    // Crosstalk:EveryNthAiring (SPEC F127.8, STORY-328, PLAN T285) — CrosstalkOptions' own
    // [Range(1, int.MaxValue)] (boot-enforced via ValidateDataAnnotations); this validator adds the
    // F53.1 settings-API-only ceiling. Floor of 1 mirrors the option's own default ("every eligible
    // airing carries banter" — 0 has no honest meaning, unlike the "0 = off" cadence knobs
    // elsewhere in this file, since Shows being empty is ALREADY the off switch for this feature).
    // 100 is a generous ceiling — "1 every 100 shows" is already indistinguishable from off.
    internal const int CrosstalkEveryNthAiringMin = 1;
    internal const int CrosstalkEveryNthAiringMax = 100;

    // Crosstalk:Shows (SPEC F127.8, STORY-328, PLAN T285 review F4) — a fat-finger guard on entry
    // count, the F53.1 ceiling shape every other array-valued key on this list already carries; no
    // real station names anywhere close to this many shows.
    internal const int CrosstalkShowsMaxCount = 50;

    // Maps each allowlisted key to a per-key (range + type) validator. An instance method (not a
    // static field) purely because the Station:Theme entry below closes over the constructor's own
    // themeCatalog — every other entry is a plain static delegate exactly as before.
    static Dictionary<string, Func<string, bool>> BuildValidators(ThemeCatalog themeCatalog) =>
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

            // Artwork/station-icon URL base (SPEC F88.4–F88.5, STORY-223) — an operator-supplied URL
            // an eventual metadata-aware player fetches, exactly PublicStreamUrl's own risk profile,
            // so it reuses the identical SSRF/markup-injection guard rather than a parallel copy.
            ["Station:PublicBaseUrl"] = IsSafePublicStreamUrl,

            // TTS endpoint (F36.1–F36.2) — there is no "disabled TTS" state, so an absolute
            // http/https URL is required; empty is rejected (mirrors TtsOptions' [Required, Url]).
            ["Tts:Endpoint"] = v => !string.IsNullOrEmpty(v) && IsAbsoluteHttpUri(v),

            // Operator pronunciation corrections (SPEC F68.5, STORY-185) — a JSON array of
            // {from, to} string pairs; empty ("[]" or blank) means no corrections and is legal.
            ["Tts:Corrections"] = IsValidCorrectionsArray,

            // Station pronunciation rules (SPEC F97.1, F97.3, STORY-253) — a JSON array of
            // {pattern, word, ipa} objects; empty ("[]" or blank) means no rules and is legal.
            ["Tts:Pronunciations"] = IsValidPronunciationsArray,

            // Piper local-fallback engine, legacy single-hop keys (SPEC F70.1, STORY-190, gh-#147:
            // ignored when a Tts:Fallback:Profiles chain is deployed; that shape is env-only and
            // startup-validated by GenWave.Tts.TtsFallbackOptionsValidator, never PUT through
            // here) — Endpoint mirrors Llm:Endpoint's own shape: empty is the legal disabled
            // state (Piper not deployed, F70.1), any non-empty value must be an absolute
            // http/https URL. Voice is free text, same "no shape to police" story as Llm:Model —
            // it is never sent on the wire for the piper engine (TtsFallbackProfile.Voice's
            // schema remarks), only compared by an operator against what the compose `piper`
            // sidecar was actually started with.
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

            // Dependency-probe cadence (SPEC F70.2 AC1/AC3/AC5, gh-#125) — all three live, all
            // three floored by DependencyHealthOptions' own [Range(1, int.MaxValue)] at boot.
            ["DependencyHealth:ProbeIntervalSeconds"] = v => IsIntInRange(v, ProbeIntervalSecondsMin, ProbeIntervalSecondsMax),
            ["DependencyHealth:ProbeTimeoutSeconds"] = v => IsIntInRange(v, ProbeTimeoutSecondsMin, ProbeTimeoutSecondsMax),
            ["DependencyHealth:UnhealthyThreshold"] = v => IsIntInRange(v, UnhealthyThresholdMin, UnhealthyThresholdMax),

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

            // Listener requests (SPEC F87.2, F87.6, STORY-224, PLAN T86) — Enabled is the F87.2 kill
            // switch (plain bool, same shape as every other surface toggle above); OverrideEnvelope
            // is the F87.6 fulfillment-bypass flag, default true. WindowMinutes mirrors
            // StationRequestsOptions' own [Range(1, int.MaxValue)] floor; F53.1 adds the ceiling.
            ["Station:Requests:Enabled"] = IsBool,
            ["Station:Requests:OverrideEnvelope"] = IsBool,
            ["Station:Requests:WindowMinutes"] = v => IsIntInRange(v, RequestsWindowMinutesMin, RequestsWindowMinutesMax),

            // Persona Catalog origin (SPEC F90.1, STORY-234, PLAN T99) — empty is the F90.1 kill
            // switch (catalog endpoints 404, admin UI hides the shelf), mirroring Llm:Endpoint/
            // Tts:Fallback:Endpoint's own "empty legal, else absolute http/https" shape.
            ["Community:CatalogIndexUrl"] = v => string.IsNullOrEmpty(v) || IsAbsoluteHttpUri(v),

            // Audience posture (SPEC F95.1, STORY-250, PLAN T111) — exactly the two values T114's
            // pool predicate will recognize; case-insensitive, mirroring Llm:DegradationPin's own
            // guard just above. No cross-field checks; no consumer reads this yet.
            ["Station:Audience"] = IsValidAudiencePosture,

            // Station timezone (gh-#117) — empty is the legal "container's own clock" state; any
            // non-empty value must resolve through TimeZoneInfo.FindSystemTimeZoneById (net10 on
            // Linux accepts IANA ids directly), so a typo is a 400 here rather than a silent
            // fall-back at prompt time.
            ["Station:Timezone"] = IsValidStationTimezone,

            // Theme selection (SPEC F102.14, F103.7, STORY-265/271, PLAN T163/T183) — the first
            // SettingKind.Choice key: membership in themeCatalog's CURRENT shipped ∪ owner slugs
            // (never labels — T175), not a shape/parseability check like every other entry above.
            // This is what makes the kind's own promise real — a value outside that set is rejected
            // HERE, at write time, rather than silently falling back to the default at read time the
            // way an unresolvable String value would (F102.6).
            ["Station:Theme"] = v => IsValidThemeSlug(v, themeCatalog),

            // Icon pack selection (SPEC F130.4, STORY-337, PLAN T303) — the SECOND SettingKind.Choice
            // key, but SHAPE-ONLY (unlike Station:Theme's own membership check just above): empty is
            // legal and is the F130.4 default (house icons); a non-empty value must be catalog-slug-
            // shaped. Existence against currently-installed station.icon_pack rows is deliberately NOT
            // checked here — there is no in-memory icon-pack catalog the way ThemeCatalog is for
            // themes (see IsValidCrosstalkShowsArray's own remarks for the identical reasoning this
            // validator already applies to Crosstalk:Shows), and F130.5's own fail-open uninstall
            // posture means a slug that stops resolving after this write is an EXPECTED, handled
            // state (house icons), never a defect a stricter write-time gate would need to prevent.
            ["Station:IconPack"] = IsValidIconPackSlug,

            // The F107 context seam (SPEC F107.2/F107.7, F108.1-F108.2, F109.1, STORY-297, PLAN
            // T226) — Context:{Key}:* per registered IContextProvider. Enabled is a plain bool kill
            // switch, same shape as every other surface toggle above.
            ["Context:Weather:Enabled"] = IsBool,
            // Weather's own SPEC F108.2 floor (30, not the generic 1) — see this class's own
            // Context:{Key}:SegmentCadenceMinutes remarks above (F2 fix, T226 review).
            ["Context:Weather:SegmentCadenceMinutes"] = v => IsIntInRange(v, WeatherSegmentCadenceMinutesMin, WeatherSegmentCadenceMinutesMax),
            ["Context:Weather:PatterCadenceMinutes"] = v => IsIntInRange(v, ContextPatterCadenceMinutesMin, ContextPatterCadenceMinutesMax),
            ["Context:Weather:PersonaId"] = v => IsIntInRange(v, ContextPersonaIdMin, int.MaxValue),
            ["Context:History:Enabled"] = IsBool,
            ["Context:History:SegmentCadenceMinutes"] = v => IsIntInRange(v, ContextSegmentCadenceMinutesMin, ContextSegmentCadenceMinutesMax),
            ["Context:History:PatterCadenceMinutes"] = v => IsIntInRange(v, ContextPatterCadenceMinutesMin, ContextPatterCadenceMinutesMax),
            ["Context:History:PersonaId"] = v => IsIntInRange(v, ContextPersonaIdMin, int.MaxValue),

            // Station broadcast location (SPEC F108.1, F108.3, PLAN T226) — free text, deliberately
            // unvalidated (StationLocation's own remarks: "blank or invalid" is
            // WeatherContextProvider's own fail-closed check, not this validator's — mirrors
            // Llm:Model/Tts:Fallback:Voice's own "no shape to police" posture).
            ["Station:Location:Latitude"] = AlwaysValid,
            ["Station:Location:Longitude"] = AlwaysValid,
            ["Station:Location:SpokenName"] = AlwaysValid,

            // Clock-anchored imaging knobs (SPEC F110.1/F110.3, gh-#381, PLAN T226) — plain bool
            // kill switches, no consumer reads them yet (Station:Audience's own T111 precedent).
            ["Station:Imaging:ClockAnchoredIdents"] = IsBool,
            ["Station:Imaging:TimeAnnouncements"] = IsBool,
            // TimeDate elapsed-due expiry budget (SPEC F124.4/F141.1, gh-#469/gh-#526, STORY-321/355,
            // PLAN T269/T326) — same "1 minimum, 1-day ceiling" shape as Context:{Key}:SegmentCadenceMinutes
            // above, expressed in seconds (F141.1).
            ["Station:Imaging:TimeAnnouncementBudgetSeconds"] =
                v => IsIntInRange(v, TimeAnnouncementBudgetSecondsMin, TimeAnnouncementBudgetSecondsMax),

            // Show-flavor patter line cadence (SPEC F116.3, STORY-308, PLAN T249) — same
            // "0 = off, 1440 ceiling" shape as Context:{Key}:PatterCadenceMinutes above.
            ["Station:Shows:PatterCadenceMinutes"] = v => IsIntInRange(v, ShowsPatterCadenceMinutesMin, ShowsPatterCadenceMinutesMax),

            // Crosstalk duration-fit target (SPEC F127.4, STORY-326, PLAN T282) — floor of 5s
            // guards a degenerate near-zero target from rejecting every exchange outright (see
            // CrosstalkDurationTargetSecondsMin's own remarks); F53.1 adds the settings-API ceiling
            // on top of CrosstalkOptions' own boot-enforced [Range(1, int.MaxValue)].
            ["Crosstalk:DurationTargetSeconds"] =
                v => IsIntInRange(v, CrosstalkDurationTargetSecondsMin, CrosstalkDurationTargetSecondsMax),

            // Crosstalk:Shows (SPEC F127.8, STORY-328, PLAN T285 review F4) — a JSON array of show
            // SLUGS, never display names/labels (T175's "names slugs, not labels" rule — the
            // Station:Theme precedent just above); empty ("[]" or blank) is legal and is the
            // fail-closed OFF state (F127.8).
            ["Crosstalk:Shows"] = IsValidCrosstalkShowsArray,

            // Crosstalk:EveryNthAiring (SPEC F127.8, STORY-328, PLAN T285) — floor mirrors
            // CrosstalkOptions' own [Range(1, int.MaxValue)]; F53.1 adds the ceiling.
            ["Crosstalk:EveryNthAiring"] =
                v => IsIntInRange(v, CrosstalkEveryNthAiringMin, CrosstalkEveryNthAiringMax),
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

        if (!validators.TryGetValue(key, out var validate))
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

    // Station:Audience (SPEC F95.1, STORY-250) — exactly "everyone" (default, fail-closed) or
    // "mature"; case-insensitive, the same shape as IsValidDegradationPin just above.
    static bool IsValidAudiencePosture(string v) =>
        v.Trim().ToLowerInvariant() is "everyone" or "mature";

    // Station:Timezone (gh-#117) — empty (container's own clock) or an id the host's timezone
    // database resolves. FindSystemTimeZoneById is the lookup's documented contract: it throws
    // TimeZoneNotFoundException for an unknown id and InvalidTimeZoneException for a corrupt one,
    // so the try/catch here IS the boolean answer, not exceptions as control flow beyond it.
    static bool IsValidStationTimezone(string v)
    {
        if (string.IsNullOrEmpty(v)) return true;

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(v);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    // Station:Theme (SPEC F102.14, F103.7, STORY-265/271, PLAN T183) — membership in
    // themeCatalog's CURRENT shipped ∪ owner slug set. TryGetBySlug is the SAME lookup
    // ThemeCatalog.Resolve itself uses — Ordinal, case-sensitive by design (slugs are kebab-case
    // identifiers, not display text) — so a value this validator accepts is, by construction, a
    // value ThemeCatalog.Resolve can actually resolve; a proposed value is always a slug, never a
    // display label (T175).
    static bool IsValidThemeSlug(string v, ThemeCatalog themeCatalog) =>
        themeCatalog.TryGetBySlug(v, out _);

    // Station:IconPack (SPEC F130.4, STORY-337, PLAN T303) — empty (house icons) or a value shaped
    // like a real catalog slug. Reuses ShowSlugFormat's own character class — the ONE catalog-slug
    // shape every kind on this house shares (PersonaController.SlugFormat/CatalogIndexValidator.SlugSegment),
    // not a show-specific one despite that method's name — rather than a third independently-authored
    // copy of the identical pattern.
    static bool IsValidIconPackSlug(string v) => v.Length == 0 || ShowSlugFormat().IsMatch(v);

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
    /// Guards <c>Station:PublicStreamUrl</c> (F62.1, F62.8) — and, identically, <c>Station:PublicBaseUrl</c>
    /// (F88.4–F88.5, STORY-223) — against both the SSRF/open-redirect class of bug and markup
    /// injection into the future public <c>&lt;audio src&gt;</c>/about panel:
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
    /// <c>SpeechCorrection</c>'s own JSON binding in <c>GenWave.Tts.SpeechCorrectionProvider</c>),
    /// plus optional string <c>whenPrecededBy</c>/<c>whenFollowedBy</c> context conditions
    /// (gh-#161) — absent or JSON null means unconditional, any other non-string value is a shape
    /// error worth rejecting here rather than letting the provider silently degrade the whole rule
    /// set. An empty array, or a blank value, is legal — "no corrections configured". A
    /// blank/whitespace <c>from</c> (or a blank context) on an individual rule is NOT rejected
    /// here: <c>SpeechCorrectionSet</c> already treats those as no-ops by design, so this validator
    /// only guards JSON shape, not rule usefulness.
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
                if (!OptionalPropertyIsString(element, "whenPrecededBy")) return false;
                if (!OptionalPropertyIsString(element, "whenFollowedBy")) return false;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates <c>Tts:Pronunciations</c> (SPEC F97.1, F97.3): a JSON array where every element is
    /// an object carrying a string <c>pattern</c> (required, mirroring <c>PronunciationRule</c>'s
    /// JSON binding in <c>GenWave.Tts.PronunciationRuleProvider</c>), plus optional string
    /// <c>word</c>/<c>ipa</c> — absent or JSON null is legal shape here, exactly like
    /// <c>Tts:Corrections</c>' optional context fields just above; a blank/missing <c>word</c> or
    /// <c>ipa</c> is NOT rejected here, <c>PronunciationRuleSet.Create</c> already degrades those
    /// (and a stray <c>)</c> in <c>ipa</c>) to a skipped rule by design, so this validator only
    /// guards JSON shape, not rule usefulness. An empty array, or a blank value, is legal — "no
    /// station pronunciation rules configured".
    /// </summary>
    static bool IsValidPronunciationsArray(string v)
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
                if (!HasStringProperty(element, "pattern")) return false;
                if (!OptionalPropertyIsString(element, "word")) return false;
                if (!OptionalPropertyIsString(element, "ipa")) return false;
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

    // Mirrors GenWave.Host.Api.PersonaController.SlugFormat's own character class (lowercase
    // letters, digits, single hyphens — the house Slugify/LegacyPersonaCardMapper output shape).
    // \A/\z, NOT ^/$ (that finding's own security-api rationale): .NET regex `$` matches immediately
    // before a trailing '\n', not only at the true end of input.
    [GeneratedRegex("\\A[a-z0-9]+(-[a-z0-9]+)*\\z")]
    private static partial Regex ShowSlugFormat();

    /// <summary>
    /// Validates <c>Crosstalk:Shows</c> (SPEC F127.8, PLAN T285 review F4): a JSON array of unique
    /// show SLUGS (lowercase-kebab, <see cref="ShowSlugFormat"/>), at most
    /// <see cref="CrosstalkShowsMaxCount"/> entries. An empty array, or a blank value, is legal — SPEC
    /// F127.8's fail-closed "empty means the feature is off" state. Existence against
    /// <c>station.show</c> is deliberately NOT checked here — unlike <see cref="IsValidThemeSlug"/>,
    /// which consults the already-DI-registered, in-memory <see cref="ThemeCatalog"/>, there is no
    /// equivalent cheap in-memory show catalog today; adding one is plumbing this task does not need
    /// (a typo'd/deleted slug simply never matches any real show, so it resolves to no eligible show
    /// via <c>GenWave.Orchestration.CrosstalkPlanner.IsShowEnabled</c>'s own fail-closed default, not
    /// a 400 here).
    /// </summary>
    static bool IsValidCrosstalkShowsArray(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return true;

        try
        {
            using var doc = JsonDocument.Parse(v);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return false;
            if (root.GetArrayLength() > CrosstalkShowsMaxCount) return false;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String) return false;
                var slug = element.GetString();
                if (slug is null || !ShowSlugFormat().IsMatch(slug)) return false;
                if (!seen.Add(slug)) return false;
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

    // Optional string field (gh-#161 context conditions): absent or JSON null is legal
    // ("unconditional"); when present it must be a string — same case-insensitive property-name
    // matching as HasStringProperty, mirroring SpeechCorrectionProvider's own JSON binding.
    static bool OptionalPropertyIsString(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null;
        }

        return true;
    }

    string BuildRangeError(string key, string value) => key switch
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
        var k when k.Equals("Tts:Pronunciations", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a JSON array of {{\"pattern\":\"...\",\"word\":\"...\",\"ipa\":\"...\"}} objects, e.g. [] or [{{\"pattern\":\"MacLeod\",\"ipa\":\"/m\\u0259\\u02c8kla\\u028ad/\"}}].",
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
        var k when k.Equals("DependencyHealth:ProbeIntervalSeconds", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {ProbeIntervalSecondsMin} and {ProbeIntervalSecondsMax} (seconds).",
        var k when k.Equals("DependencyHealth:ProbeTimeoutSeconds", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {ProbeTimeoutSecondsMin} and {ProbeTimeoutSecondsMax} (seconds).",
        var k when k.Equals("DependencyHealth:UnhealthyThreshold", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {UnhealthyThresholdMin} and {UnhealthyThresholdMax} (consecutive failures).",
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
        var k when k.Equals("Station:PublicStreamUrl", StringComparison.OrdinalIgnoreCase) ||
                   k.Equals("Station:PublicBaseUrl", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be empty, an absolute http/https URL, " +
               "or a same-origin root-relative path starting with a single '/' (not '//'); no '\"', '<', '>', '\\', control characters, or whitespace.",
        var k when k.Equals("Llm:DegradationPin", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be one of: auto, normal, soft, hard.",
        var k when k.Equals("Station:Envelope:Genres", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a JSON array of non-blank genre names, e.g. [] or [\"Rock\",\"Jazz\"].",
        var k when k.Equals("Station:Envelope:EnergyMin", StringComparison.OrdinalIgnoreCase) ||
                   k.Equals("Station:Envelope:EnergyMax", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a number in [{EnvelopeEnergyMin}, {EnvelopeEnergyMax}].",
        var k when k.Equals("Station:Requests:Enabled", StringComparison.OrdinalIgnoreCase) ||
                   k.Equals("Station:Requests:OverrideEnvelope", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a boolean (true/false).",
        var k when k.Equals("Station:Requests:WindowMinutes", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {RequestsWindowMinutesMin} and {RequestsWindowMinutesMax} (minutes).",
        var k when k.Equals("Community:CatalogIndexUrl", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an absolute http/https URL, or empty to disable the Persona Catalog entirely.",
        var k when k.Equals("Station:Audience", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be one of: everyone, mature.",
        var k when k.Equals("Station:Timezone", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an IANA timezone id this host " +
               "recognizes (e.g. America/Edmonton), or empty to use the container's own clock.",
        var k when k.Equals("Station:Theme", StringComparison.OrdinalIgnoreCase)
            // Names SLUGS, not labels (T175) — a label is never a settable value, so listing one
            // here would be actively misleading about what the operator can actually type/PUT.
            // Lists the CURRENT shipped ∪ owner set (PLAN T183), not a frozen shipped-only
            // snapshot — an owner theme imported after boot (T184) belongs in this message the
            // moment it becomes selectable.
            => $"Value '{value}' is not valid for '{key}'. Must be one of the available theme " +
               $"slugs: {string.Join(", ", themeCatalog.All.Select(t => t.Slug))}.",
        var k when k.Equals("Station:IconPack", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be empty (house icons), or a lowercase, " +
               "hyphen-separated slug (e.g. line-icons).",
        var k when k.Equals("Context:Weather:Enabled", StringComparison.OrdinalIgnoreCase) ||
                   k.Equals("Context:History:Enabled", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a boolean (true/false).",
        var k when k.Equals("Context:Weather:SegmentCadenceMinutes", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {WeatherSegmentCadenceMinutesMin} and {WeatherSegmentCadenceMinutesMax} (minutes) — {WeatherSegmentCadenceMinutesMin} is SPEC F108.2's enforced floor (twice an hour, at most).",
        var k when k.Equals("Context:History:SegmentCadenceMinutes", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {ContextSegmentCadenceMinutesMin} and {ContextSegmentCadenceMinutesMax} (minutes).",
        var k when k.Equals("Context:Weather:PatterCadenceMinutes", StringComparison.OrdinalIgnoreCase) ||
                   k.Equals("Context:History:PatterCadenceMinutes", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {ContextPatterCadenceMinutesMin} and {ContextPatterCadenceMinutesMax} (minutes); 0 disables patter for this provider.",
        var k when k.Equals("Context:Weather:PersonaId", StringComparison.OrdinalIgnoreCase) ||
                   k.Equals("Context:History:PersonaId", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a non-negative integer; 0 defers to the on-air DJ.",
        var k when k.Equals("Station:Imaging:ClockAnchoredIdents", StringComparison.OrdinalIgnoreCase) ||
                   k.Equals("Station:Imaging:TimeAnnouncements", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a boolean (true/false).",
        var k when k.Equals("Station:Shows:PatterCadenceMinutes", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {ShowsPatterCadenceMinutesMin} and {ShowsPatterCadenceMinutesMax} (minutes); 0 disables the show-flavor line.",
        var k when k.Equals("Crosstalk:DurationTargetSeconds", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {CrosstalkDurationTargetSecondsMin} and {CrosstalkDurationTargetSecondsMax} (seconds).",
        var k when k.Equals("Crosstalk:Shows", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be a JSON array of unique show SLUGS " +
               $"(lowercase letters, digits, single hyphens — not display names), at most {CrosstalkShowsMaxCount} " +
               "entries, e.g. [] or [\"morning-drive\"]. Empty means the feature is off.",
        var k when k.Equals("Crosstalk:EveryNthAiring", StringComparison.OrdinalIgnoreCase)
            => $"Value '{value}' is not valid for '{key}'. Must be an integer between {CrosstalkEveryNthAiringMin} and {CrosstalkEveryNthAiringMax} (airings).",
        _ => $"Value '{value}' is not valid for '{key}'.",
    };
}
