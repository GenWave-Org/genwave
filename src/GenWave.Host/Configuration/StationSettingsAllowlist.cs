namespace GenWave.Host.Configuration;

/// <summary>
/// Canonical allowlist of operator-editable settings that may be persisted in
/// <c>station.settings</c> and surfaced through the overlay configuration provider.
///
/// Secrets (<c>Admin:Password</c>, <c>ConnectionStrings:*</c>,
/// <c>ICECAST_SOURCE_PASSWORD</c>, etc.) are deliberately absent — they are env-only and
/// must never enter the DB store.
///
/// W5 (the settings API) consumes this list for GET (what the operator can read) and
/// PUT (what the operator can write). Only keys present here are loaded by the provider.
///
/// Each entry carries <see cref="AllowedSetting.Kind"/> and <see cref="AllowedSetting.Unit"/>
/// so the admin UI can render the appropriate input control with a unit hint without
/// hard-coding per-key knowledge in the front end.
/// </summary>
public static class StationSettingsAllowlist
{
    /// <summary>All operator-editable settings as an ordered list.</summary>
    public static readonly IReadOnlyList<AllowedSetting> All = new AllowedSetting[]
    {
        // ── Live knobs (IOptionsMonitor re-binds without restart) ─────────────────
        new("Loudness:TargetLufs",                            SettingApplyMode.Live,          SettingKind.Number,     "LUFS"),
        new("Loudness:CeilingDbtp",                           SettingApplyMode.Live,          SettingKind.Number,     "dBTP"),

        // Station identity (SPEC F44.1, F44.2, F44.5, closes gitea-#196) — read live through
        // IStationIdentityProvider by the Orchestrator (SegmentRequest stamping), AuthController
        // (GET /api/stations), and the playout push path, so a PUT here applies with no api
        // restart. Station:Name is the ONE exception to "live means no caveat": the Icecast
        // stream/directory name (icy-name, STATION_NAME env) only catches up on the next ENGINE
        // restart — the admin UI badges this via FIELD_HELP_TEXT (SPEC F44.5), not a different
        // apply-mode; the api-side effects (patter, /api/stations, this console) are genuinely live.
        new("Station:Name",                                   SettingApplyMode.Live,          SettingKind.String,     ""),
        new("Station:Voice",                                  SettingApplyMode.Live,          SettingKind.String,     ""),

        new("Station:Cadence:LeadInBeforeEachTrack",          SettingApplyMode.Live,          SettingKind.Boolean,    ""),
        new("Station:Cadence:BackAnnounceAfterEachTrack",     SettingApplyMode.Live,          SettingKind.Boolean,    ""),
        new("Station:Cadence:StationIdEveryNUnits",           SettingApplyMode.Live,          SettingKind.Number,     "count"),
        // Main rotation scope — live so a PUT takes effect on the very next /media/random call
        // without an api restart.  An empty list equals a silent station; SettingValidator
        // rejects it on the live-edit path (F23.1).
        new("Station:Scope:LibraryIds",                       SettingApplyMode.Live,          SettingKind.NumberList, ""),
        // Safe-rotation scope — live so a K4 PUT takes effect on the next /internal/safe-track
        // call without an api restart.  NumberList mirrors the long[] shape; K4 wires the PUT
        // validation and the SettingValidator entry.
        new("Station:SafeScope:LibraryIds",                   SettingApplyMode.Live,          SettingKind.NumberList, ""),
        // Active DJ persona (SPEC F35.2, F36.2) — live so activating/deactivating a persona (or
        // PersonaController's delete-clears-active write, F35.5) takes effect on the very next
        // render, no api restart.  0 = none; a stale id is legal (ActivePersonaAccessor degrades
        // with a WARN) so the validator only checks non-negativity, never row existence.
        new("Station:Persona:ActiveId",                       SettingApplyMode.Live,          SettingKind.Number,     ""),

        // Rotation anti-repeat/artist-separation knobs (SPEC F41.6, closes gitea-#210/gitea-#213) — live so a
        // PUT here reaches the very next selection (Orchestrator) / ring write (PlayoutFeeder) with
        // no api restart. 0 legally disables either knob.
        new("Station:Rotation:RecentWindow",                  SettingApplyMode.Live,          SettingKind.Number,     "tracks"),
        new("Station:Rotation:ArtistSeparation",              SettingApplyMode.Live,          SettingKind.Number,     "tracks"),

        // Station-default segment envelope (SPEC F80.1, F81.1/F81.3, STORY-212) — the v1 24/7,
        // no-schedule-grid envelope the eventual envelope-only provider (a later task) consumes.
        // Genres is a JSON-encoded array of genre names stored as ONE opaque string-kind value —
        // same idiom as Tts:Corrections just below (the overlay only expands stored arrays into
        // indexed keys for arrays it already knows to bind as a typed list); empty/blank/"[]" means
        // no genre constraint (F81.1). EnergyMin/EnergyMax are the [0,1] percentile band (F80.1);
        // 0/1 is the full range, i.e. no energy constraint. Live so a PUT reaches the envelope-only
        // provider's very next pick with no api restart, once that provider exists.
        new("Station:Envelope:Genres",                       SettingApplyMode.Live,          SettingKind.String,     ""),
        new("Station:Envelope:EnergyMin",                     SettingApplyMode.Live,          SettingKind.Number,     ""),
        new("Station:Envelope:EnergyMax",                     SettingApplyMode.Live,          SettingKind.Number,     ""),

        // Spectator surface (SPEC F62.1, F62.8, STORY-167/170) — both read live via
        // IOptionsMonitor<StationOptions> by SurfaceGateMiddleware (SpectatorMode) and the
        // spectator "about" endpoint (PublicStreamUrl), so a PUT here reaches the very next
        // request with no api restart. SpectatorMode is the F62.1 kill switch (false = every
        // SpectatorSurfaceAttribute route 404s, the surface does not exist); PublicStreamUrl is
        // legally empty (the about panel hides the player until the operator sets it).
        new("Station:SpectatorMode",                          SettingApplyMode.Live,          SettingKind.Boolean,    ""),
        new("Station:PublicStreamUrl",                        SettingApplyMode.Live,          SettingKind.String,     ""),

        // Artwork/station-icon URL base (SPEC F88.4–F88.5, STORY-223, PLAN T85) — read live via
        // IOptionsMonitor<StationOptions> by ArtworkUrlResolver on every feeder push, so a PUT here
        // reaches the very next push with no api restart. Empty is legal and is the default: F88.5's
        // contract is that NO url= annotation is ever emitted (music or TTS) while this is blank,
        // exactly mirroring PublicStreamUrl's "empty hides the player" shape just above.
        new("Station:PublicBaseUrl",                          SettingApplyMode.Live,          SettingKind.String,     ""),

        // Listener requests (SPEC F87.2, F87.6, STORY-224, PLAN T86) — the three live-editable
        // knobs on StationRequestsOptions (the rest of the F87 throttle surface binds from the
        // env/compose-only RequestsOptions instead, deliberately absent from this allowlist).
        // Enabled is the F87.2 kill switch: false ⇒ the endpoint 404s (F61 surface-off semantics),
        // never a distinguishable "requests are closed" response — read live via
        // IOptionsMonitor<StationOptions> by the T87 intake endpoint, so a PUT here reaches the
        // very next request with no api restart. OverrideEnvelope (default true) governs whether a
        // matched request bypasses envelope genre/energy and rotation-recency at fulfillment
        // (T90). WindowMinutes is how long an unfulfilled request stays live before expiring.
        new("Station:Requests:Enabled",                       SettingApplyMode.Live,          SettingKind.Boolean,    ""),
        new("Station:Requests:OverrideEnvelope",               SettingApplyMode.Live,          SettingKind.Boolean,    ""),
        new("Station:Requests:WindowMinutes",                  SettingApplyMode.Live,          SettingKind.Number,     "minutes"),

        // TTS/LLM endpoint liveness (SPEC F36.1–F36.4, T8): KokoroTtsSynthesizer/KokoroVoiceLister
        // and LlmCopyWriter read these via IOptionsMonitor per call (no boot-frozen BaseAddress), so
        // a PUT here reroutes the very next render/voices call — no api restart. Llm:Endpoint is
        // legally empty (F34.2 — blurbs stay templated); Tts:Endpoint is not, since there is no
        // "disabled TTS" state. Llm:ApiKey is deliberately absent from this list — env-only secret
        // (F19.3), never readable or writable through this API (see SettingValidator's rejection).
        new("Tts:Endpoint",                                   SettingApplyMode.Live,          SettingKind.String,     ""),
        // Operator pronunciation corrections (SPEC F68.1, F68.5, STORY-185) — a JSON-encoded array
        // of {from, to} pairs stored as ONE opaque string-kind value (the overlay only expands a
        // stored array into indexed keys for arrays of SCALARS, not objects — see
        // StationSettingsConfigurationProvider.ExtractArrayItems). SpeechCorrectionProvider
        // (GenWave.Tts) reads it via IOptionsMonitor<TtsCorrectionsOptions> and rebuilds the
        // compiled SpeechCorrectionSet on every change — a PUT here reaches the very next render
        // with no api restart.
        new("Tts:Corrections",                                SettingApplyMode.Live,          SettingKind.String,     ""),
        // Piper local-fallback engine (SPEC F70.1, STORY-190): FallbackTtsSynthesizer
        // (GenWave.Tts) reads both via IOptionsMonitor<TtsFallbackOptions> per render, so a PUT
        // here reaches the very next render with no api restart. Empty Endpoint is legal and is
        // the disabled state — Piper not deployed, routing stays Kokoro-only (zero behavior
        // change); the shipped compose.yaml sets a real value for its own `piper` sidecar. Voice is
        // documentation only (see TtsFallbackOptions' own remarks) — it is never sent on the wire.
        new("Tts:Fallback:Endpoint",                          SettingApplyMode.Live,          SettingKind.String,     ""),
        new("Tts:Fallback:Voice",                             SettingApplyMode.Live,          SettingKind.String,     ""),
        // Per-kind TTS engine override map (SPEC F70.3, STORY-191): a JSON-encoded object mapping
        // SegmentKind names to an engine name ("kokoro"/"piper"), e.g.
        // {"StationId":"piper","LeadIn":"kokoro"} — the same "single opaque string-kind setting"
        // pattern as Tts:Corrections just above (the overlay only expands stored JSON ARRAYS into
        // indexed keys, not objects). GenWave.Tts.TtsEngineByKindProvider reads it via
        // IOptionsMonitor<TtsEngineByKindOptions> and rebuilds the compiled TtsEngineOverrideMap on
        // every change — a PUT here reaches the very next render with no api restart. Empty/absent
        // is legal and is the default (F70.3): every kind falls through to the existing F70.1
        // health-based Kokoro/Piper routing, unchanged.
        new("Tts:EngineByKind",                               SettingApplyMode.Live,          SettingKind.String,     ""),
        new("Llm:Endpoint",                                   SettingApplyMode.Live,          SettingKind.String,     ""),
        new("Llm:Model",                                      SettingApplyMode.Live,          SettingKind.String,     ""),
        new("Llm:TimeoutSeconds",                             SettingApplyMode.Live,          SettingKind.Number,     "seconds"),

        // F44.2 allowlist completion (closes gitea-#197) — six more boot-frozen consumers migrate to a
        // live provider/IOptionsMonitor read at use time:
        //   • Tts:RenderBudgetSeconds — the Orchestrator's own copy used to be a TimeSpan computed
        //     once in Program.cs; it now reads IRenderBudgetProvider fresh per unit. TtsPreviewController
        //     and SafeSegmentsController already read IOptionsMonitor<TtsOptions> per call (T8/F29) —
        //     unaffected by this change, just newly reachable through the settings API.
        //   • Tts:BlurbRetentionHours — TtsSegmentSource's blurb GC sweep now reads
        //     IOptionsMonitor<TtsOptions>.CurrentValue per render instead of a frozen field (F34.6).
        //   • Llm:MaxCopyChars — LlmCopyWriter already reads IOptionsMonitor<LlmOptions> fresh per
        //     completion (F36.2); this just adds the key to the allowlist.
        //   • Admin:PlayHistoryCapacity — PlayHistoryService now reads IOptionsMonitor<AdminOptions>
        //     at Push() time; the ring trims to the new capacity on the very next push.
        //   • Library:ScanIntervalSeconds — ScanService re-reads IOptionsMonitor<LibraryOptions> and
        //     retunes its PeriodicTimer.Period before every tick.
        //   • Library:EnrichmentConcurrency — EnrichmentService's worker pool is reconciled toward
        //     the live value on the same cadence as its backfill loop; growing spawns workers
        //     immediately, shrinking is cooperative (a worker retires between items, so anything
        //     already in flight always finishes under the value it started with).
        // Boot-floor note (the V6 "nested DataAnnotations are dead at boot" lesson, applied here):
        // Tts:RenderBudgetSeconds/BlurbRetentionHours and Llm:MaxCopyChars are TOP-LEVEL properties
        // on TtsOptions/LlmOptions, both bound via .AddOptions<T>().ValidateDataAnnotations().ValidateOnStart()
        // in Program.cs, so their [Range(1, int.MaxValue)] attributes are genuinely enforced at boot.
        // Admin:PlayHistoryCapacity and the two Library:* keys below have NO bound IValidateOptions at
        // all (AdminOptions/LibraryOptions are wired via plain Configure<T>, never ValidateDataAnnotations) —
        // SettingValidator is the ONLY floor-enforcement surface for these three at either boot or
        // live-edit time, exactly the existing GW_XFADE_*/GW_SAFE_GAP_SECONDS precedent ("No bound
        // options class; rules enforced purely in this validator").
        new("Tts:RenderBudgetSeconds",                        SettingApplyMode.Live,          SettingKind.Number,     "seconds"),
        new("Tts:BlurbRetentionHours",                        SettingApplyMode.Live,          SettingKind.Number,     "hours"),
        new("Llm:MaxCopyChars",                               SettingApplyMode.Live,          SettingKind.Number,     "chars"),
        new("Admin:PlayHistoryCapacity",                      SettingApplyMode.Live,          SettingKind.Number,     "entries"),
        new("Library:ScanIntervalSeconds",                    SettingApplyMode.Live,          SettingKind.Number,     "seconds"),
        new("Library:EnrichmentConcurrency",                  SettingApplyMode.Live,          SettingKind.Number,     "workers"),

        // Scan availability grace (SPEC F58.3, closes gitea-#223) — ScanService reads
        // IOptionsMonitor<ScanOptions>.CurrentValue fresh per tick, the SAME live shape as
        // Library:ScanIntervalSeconds directly above (a live PUT governs the very next scan tick's
        // missing-diff, no api restart), so this carries the identical Live apply-mode badge.
        new("Library:Scan:MissThreshold",                     SettingApplyMode.Live,          SettingKind.Number,     "misses"),

        // MusicBrainz year lookup (SPEC F48.5, X5, closes gitea-#208) — Enabled/Endpoint are read fresh
        // per backfill tick/call via IOptionsMonitor<YearLookupOptions> (MusicBrainzYearLookup/
        // EnrichmentService.BackfillYearLookupAsync), the same F36.2 typed-client shape as
        // Tts:Endpoint/Llm:Endpoint above — a PUT here reaches the very next tick, no api restart.
        // Enabled is the kill switch: false stops claiming before the next tick.
        new("Library:YearLookup:Enabled",                     SettingApplyMode.Live,          SettingKind.Boolean,    ""),
        new("Library:YearLookup:Endpoint",                    SettingApplyMode.Live,          SettingKind.String,     ""),
        // MinScore only changes behavior the next time a row is looked up (an already-stamped
        // row's outcome is not retroactively re-judged) — the F44.3 Enrichment apply-mode, same
        // badge as the CueDetection/Energy pair below.
        new("Library:YearLookup:MinScore",                    SettingApplyMode.Enrichment,    SettingKind.Number,     "score"),

        // ── Engine-restart knobs (Liquidsoap env vars; effective on next engine boot) ──
        new("GW_XFADE_MIN",         SettingApplyMode.EngineRestart, SettingKind.Number, "seconds"),
        new("GW_XFADE_MAX",         SettingApplyMode.EngineRestart, SettingKind.Number, "seconds"),
        // Inter-safe-track silence gap (F29.6/F29.8, STORY-100) — mirrors GW_XFADE_* exactly:
        // same wire key naming, same EngineRestart apply mode, same Number kind/seconds unit.
        new("GW_SAFE_GAP_SECONDS",  SettingApplyMode.EngineRestart, SettingKind.Number, "seconds"),

        // ── Enrichment-mode knobs (F44.3): consumed only when a file is (re-)analyzed. Both are
        // TOP-LEVEL properties on CueDetectionOptions/EnergyOptions, but neither options class has
        // ANY bound IValidateOptions (plain Configure<T> in MediaLibraryServiceCollectionExtensions) —
        // same "no boot floor beyond SettingValidator" story as the three Admin/Library live keys
        // above. FfmpegCueAnalyzer/FfmpegEnergyAnalyzer read IOptionsMonitor<T>.CurrentValue fresh
        // per AnalyzeAsync call, so an edit here is visible on the NEXT enrichment, never retroactive
        // for an already-enriched row.
        new("Library:CueDetection:MinSilenceDurationSec",     SettingApplyMode.Enrichment,    SettingKind.Number,     "seconds"),
        new("Library:Energy:WindowSeconds",                   SettingApplyMode.Enrichment,    SettingKind.Number,     "seconds"),

        // LLM degradation pin (SPEC F69.3, STORY-188) — DegradationController (GenWave.Tts) reads
        // this fresh via IOptionsMonitor<LlmOptions> on every evaluation, so a live PUT here
        // pins/unpins the mode with no api restart. "auto" (the LlmOptions default) leaves the
        // mode fully automatic; "normal"/"soft"/"hard" holds it.
        new("Llm:DegradationPin",                             SettingApplyMode.Live,          SettingKind.String,     ""),
    };

    /// <summary>All operator-editable settings, keyed by configuration key.</summary>
    public static readonly IReadOnlyDictionary<string, AllowedSetting> ByKey =
        All.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);
}
