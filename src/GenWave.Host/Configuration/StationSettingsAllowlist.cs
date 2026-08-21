using GenWave.Host.Theming;

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
    /// <summary>
    /// Every SHIPPED theme, as a slug/display-name <see cref="SettingChoice"/> pair (STORY-263), in
    /// <see cref="ThemeCatalog.LoadShipped"/>'s load order. Carried on the <c>Station:Theme</c>
    /// entry's <see cref="AllowedSetting.Choices"/> below purely so that record stays non-null the
    /// way every other <see cref="SettingKind.Choice"/> entry's contract promises — this frozen
    /// snapshot is never what an operator actually sees or can PUT (PLAN T183 superseded it as a
    /// live source): both call sites that decide what is selectable/acceptable
    /// (<see cref="GenWave.Host.Api.SettingsController"/>'s GET/PUT response, <see cref="SettingValidator"/>'s
    /// own guard) resolve the CURRENT shipped ∪ owner set from the DI-registered
    /// <see cref="ThemeCatalog"/> singleton instead — see <see cref="ThemeChoices"/> — every
    /// request, so an owner theme imported after boot (T184) or folded in by
    /// <c>ThemeCatalogOwnerLoadHostedService</c>'s own boot warm-up becomes selectable with no
    /// process restart.
    ///
    /// This static table has no DI container, so it cannot ask for that runtime singleton — it
    /// loads its OWN copy the same way <c>Program.cs</c>'s own boot-time canary does, via
    /// <see cref="ThemeCatalog.LoadShipped"/>, which reads embedded resources baked into this
    /// assembly at build time and needs no runtime service to do it. The two loads are cheap,
    /// deterministic, and always agree (same assembly, same resources) — a duplicate parse, not a
    /// duplicate source of truth; they can only ever diverge from each other in the direction the
    /// runtime catalog growing owner rows adds on top, never in the shipped half.
    /// </summary>
    static readonly IReadOnlyList<SettingChoice> ShippedThemeChoices =
        ThemeCatalog.LoadShipped().All
            .Select(theme => new SettingChoice(theme.Slug, theme.Name, theme.Slug == ThemeCatalog.ShippedDefaultSlug))
            .ToList();

    /// <summary>
    /// Computes <c>Station:Theme</c>'s LIVE choices from <paramref name="themeCatalog"/> — the
    /// DI-registered runtime instance's current shipped ∪ owner set (SPEC F103.7, STORY-271, PLAN
    /// T183), evaluated fresh every call rather than read off the frozen
    /// <see cref="ShippedThemeChoices"/> snapshot above. Mapping and
    /// <see cref="SettingChoice.IsDefault"/> semantics are identical to that snapshot's own: an
    /// explicit slug match against <see cref="ThemeCatalog.ShippedDefaultSlug"/>, never list/load-
    /// order position, so it stays correct regardless of how many owner themes have folded in or
    /// where they land in <see cref="ThemeCatalog.All"/>'s order.
    ///
    /// <para>
    /// <b>Provenance (SPEC F103.11, PLAN T187; review F3/F4).</b> Each choice also carries
    /// <see cref="SettingChoice.ImportedFrom"/>/<see cref="SettingChoice.ImportedAt"/>, read off
    /// <see cref="ThemeCatalog.Entries"/> — one pass, no second per-item lookup back into the
    /// catalog — <see langword="null"/> for a shipped default (no owner row exists), non-null for a
    /// catalog- or file-imported one. The admin UI's Settings page lists every choice carrying this
    /// with its own row — "&lt;label&gt; — Imported · &lt;source&gt; · &lt;date&gt;", the
    /// <c>station.persona</c>/db-25 pattern applied to the theme kind (mirrors
    /// <c>PersonaDto.ImportedFrom</c>/<c>ImportedAt</c> riding the same GET/PUT projection every
    /// other persona field does).
    /// </para>
    /// </summary>
    public static IReadOnlyList<SettingChoice> ThemeChoices(ThemeCatalog themeCatalog) =>
        themeCatalog.Entries
            .Select(entry => new SettingChoice(
                entry.Theme.Slug, entry.Theme.Name, entry.Theme.Slug == ThemeCatalog.ShippedDefaultSlug,
                entry.Provenance?.ImportedFrom, entry.Provenance?.ImportedAt))
            .ToList();

    /// <summary>
    /// Computes <c>Station:IconPack</c>'s LIVE choices from every currently installed pack's own SLUG
    /// (SPEC F130.4, STORY-337, PLAN T303) — the second <see cref="SettingKind.Choice"/> entry,
    /// mirroring <see cref="ThemeChoices"/>'s "resolve fresh, every call, off the real store" shape
    /// rather than this static table's own frozen snapshot. Unlike a theme (a build-time embedded
    /// resource plus an in-memory <see cref="ThemeCatalog"/> singleton an owner import folds into), an
    /// icon pack has NO shipped set and NO in-memory catalog of its own — every non-default choice is an
    /// installed <c>station.icon_pack</c> row's own slug, read fresh off
    /// <see cref="GenWave.Core.Abstractions.IIconPackStore.GetAllSlugsAsync"/> by the caller
    /// (<c>SettingsController</c>) and handed in here already fetched (PLAN T303 review — the settings
    /// hot path needs nothing past the slug, never the full pack row's own up-to-256-KiB definition
    /// text), the same "the caller owns the I/O, this method only shapes it" split <see cref="ThemeChoices"/>
    /// keeps between itself and its own DI-registered <see cref="ThemeCatalog"/> argument.
    ///
    /// <para>
    /// LABEL = SLUG for every installed pack (unlike <see cref="ThemeChoices"/>'s own manifest-carried
    /// display name): SPEC F130.1's <c>gw-icon-pack</c> document has no pack-level display-name field
    /// at all — style plus an icons map, nothing else — so the slug IS the only honest label this
    /// schema can offer (mirrors <c>SettingValidator.IsValidCrosstalkShowsArray</c>'s own "names slugs,
    /// not labels" T175 rule applied to a value that, here, has no separate label to begin with).
    /// </para>
    ///
    /// <para>
    /// <b>THE FIRST CHOICE IS ALWAYS <c>("", "House icons")</c>, FLAGGED <see cref="SettingChoice.IsDefault"/>
    /// (review finding F1 — corrects the T303-as-built shape, which returned an EMPTY list for a
    /// station with no packs installed, exactly the "zero choices" state
    /// <c>ChoiceSettingControl</c>'s own guard treats as a wiring bug and refuses to render at all,
    /// which every fresh station legitimately is on day one).</b> <c>Station:IconPack</c>'s empty
    /// value is not merely "unset" the way an unseeded <see cref="ThemeChoices"/> slug would be — it
    /// is itself a SELECTABLE, permanent member of the closed set (SPEC F130.4's own house-icons
    /// fallback), so it earns a real <see cref="SettingChoice"/> row rather than living only in
    /// <see cref="ChoiceSettingControl"/>'s own "unset" branch the way <see cref="ThemeChoices"/>'s
    /// shipped default does. This is also what makes the dropdown WORK on a station with zero packs
    /// installed — the single most common case, every station until an operator installs one — instead
    /// of always rendering <c>ChoiceSettingControl</c>'s own "no choices available" alert.
    /// </para>
    /// </summary>
    public static IReadOnlyList<SettingChoice> IconPackChoices(IReadOnlyList<string> installedSlugs)
    {
        var choices = new List<SettingChoice>(installedSlugs.Count + 1)
        {
            new("", "House icons", IsDefault: true),
        };
        choices.AddRange(installedSlugs.Select(slug => new SettingChoice(slug, slug)));
        return choices;
    }

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
        // Station pronunciation rules (SPEC F97.1, F97.3, STORY-253) — a JSON-encoded array of
        // {pattern, word, ipa} rules stored as ONE opaque string-kind value, the identical
        // "overlay can't expand an array of objects" idiom as Tts:Corrections just above.
        // PronunciationRuleProvider (GenWave.Tts) reads it via IOptionsMonitor<TtsPronunciationsOptions>
        // and rebuilds the compiled PronunciationRuleSet on every change — a PUT here reaches the
        // very next render with no api restart. Merged with the active persona card's own rules,
        // card winning on conflict (F97.4) — see PersonaOverStationMerge.
        new("Tts:Pronunciations",                             SettingApplyMode.Live,          SettingKind.String,     ""),
        // Piper local-fallback engine, LEGACY single-hop shape (SPEC F70.1, STORY-190, gh-#147):
        // FallbackTtsSynthesizer (GenWave.Tts) reads both via IOptionsMonitor<TtsFallbackOptions>
        // per render, so a PUT here reaches the very next render with no api restart. These two
        // keys form the implicit one-piper-hop fallback chain; a deployment-config
        // Tts:Fallback:Profiles chain (env/appsettings only — an array of objects, which the
        // settings overlay cannot store) supersedes them entirely. Empty Endpoint is legal and is
        // the disabled state — Piper not deployed, routing stays Kokoro-only (zero behavior
        // change); the shipped compose.yaml sets a real value for its own `piper` sidecar. Voice is
        // documentation only (see TtsFallbackProfile.Voice's schema remarks) — never sent on the
        // wire for the piper engine.
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

        // Dependency health probes (SPEC F70.2 AC1/AC3/AC5, gh-#125) — DependencyHealthProbeService
        // hands the prober a Func<DependencyProbeCadence> that reads IOptionsMonitor fresh, and the
        // prober re-evaluates it every cycle (retuning PeriodicTimer.Period after each tick, the
        // same live shape as Library:ScanIntervalSeconds above), so a PUT here reaches the very next
        // probe with no api restart.
        //
        // These three were deliberately EXCLUDED from this allowlist when F70.2 shipped —
        // "deployment tuning, not operator-editable station config". gh-#125 reversed that: chasing
        // a flapping Kokoro probe on a live station meant a compose edit and a redeploy to move one
        // number, twice. Nothing here is a secret and nothing is engine-side, so the original
        // exclusion bought no safety — only friction during an incident.
        //
        // Boot-floor note: DependencyHealthOptions IS wired via .AddOptions<T>()
        // .ValidateDataAnnotations().ValidateOnStart() in Program.cs, so all three [Range(1, ...)]
        // attributes are genuinely enforced at boot; SettingValidator adds the F53.1 ceilings that
        // only apply on the settings-API path.
        new("DependencyHealth:ProbeIntervalSeconds",          SettingApplyMode.Live,          SettingKind.Number,     "seconds"),
        new("DependencyHealth:ProbeTimeoutSeconds",           SettingApplyMode.Live,          SettingKind.Number,     "seconds"),
        new("DependencyHealth:UnhealthyThreshold",            SettingApplyMode.Live,          SettingKind.Number,     "failures"),

        // LLM degradation pin (SPEC F69.3, STORY-188) — DegradationController (GenWave.Tts) reads
        // this fresh via IOptionsMonitor<LlmOptions> on every evaluation, so a live PUT here
        // pins/unpins the mode with no api restart. "auto" (the LlmOptions default) leaves the
        // mode fully automatic; "normal"/"soft"/"hard" holds it.
        new("Llm:DegradationPin",                             SettingApplyMode.Live,          SettingKind.String,     ""),

        // Persona Catalog origin (SPEC F90.1, STORY-234, PLAN T99) — CommunityCatalogAccessor reads
        // this fresh via IOptionsMonitor<CommunityOptions>, so a live PUT here reaches the very next
        // catalog request with no api restart (T101). Defaults to the official genwave-catalog
        // index.json; EMPTY is the F90.1 fail-closed kill switch — both catalog endpoints 404 and
        // the admin UI hides the shelf, the same F87.2/F61 surface-off idiom as every other kill
        // switch on this list.
        new("Community:CatalogIndexUrl",                      SettingApplyMode.Live,          SettingKind.String,     ""),

        // Audience posture (SPEC F95.1, STORY-250, PLAN T111) — everyone (default, fail-closed) |
        // mature. Live so a PUT here reaches the very next selection query with no api restart,
        // once T114 wires the shared pool predicate (rotation, request matcher, boundary bias —
        // F95.4). No consumers yet: this task only adds the allowlist entry, the StationOptions
        // property, and the SettingValidator guard.
        new("Station:Audience",                               SettingApplyMode.Live,          SettingKind.String,     ""),

        // Station timezone (gh-#117; extended gh-#224) — an IANA id (e.g. America/Edmonton) every
        // "station-local now" read (LLM/patter clocks, the schedule grid's slot resolution, taste
        // day/hour gating) resolves through IStationClockProvider; empty (the default) is
        // the honest "container's own clock" state, pre-gh-#117 behavior unchanged. Live so a PUT
        // here reaches the very next prompt build / SegmentRequest stamp with no api restart —
        // OptionsMonitorStationClockProvider re-resolves IOptionsMonitor<StationOptions> per call.
        new("Station:Timezone",                               SettingApplyMode.Live,          SettingKind.String,     ""),

        // Theme selection (SPEC F102.14, F102.15, F103.7, STORY-265/271, PLAN T163/T183) — closed
        // CHOICE, not free text: a typo in a String value would silently fail to resolve (F102.6's
        // fallback would mask it rather than reject it), so this is the first SettingKind.Choice
        // entry. The Choices carried HERE (ShippedThemeChoices, above) are a structural placeholder
        // only — SettingsController and SettingValidator both re-source the live set from the
        // DI-registered ThemeCatalog via ThemeChoices(ThemeCatalog) instead (PLAN T183), so an owner
        // theme import (T184) widens what is selectable/acceptable with no restart and no second
        // edit here. Live: the eventual resolution provider (T164) reads it via IOptionsMonitor per request, same shape
        // as Station:PublicStreamUrl/Station:SpectatorMode. Nothing reads this key's VALUE yet —
        // T164 is what wires resolution; T165 is what proves it live on a running stack. Declaring
        // it now, unread, is deliberate (T163's own scope). Deliberately UNSEEDED in
        // appsettings.json — the precedence chain (visitor cookie → settings row → env default →
        // shipped default, F102.5) already terminates at ThemeCatalog.ShippedDefaultSlug without a
        // config entry here; seeding this key would duplicate that floor as a literal nothing
        // enforces against ThemeCatalog's own const, and would shadow F102.5's "no value anywhere"
        // case so that branch of T164's resolution logic never actually fires against a real
        // deployment. A fresh deploy with no key present resolves to the shipped default exactly
        // because the chain has a floor, not because appsettings.json states one.
        new("Station:Theme",                                  SettingApplyMode.Live,          SettingKind.Choice,     "", ShippedThemeChoices),

        // Icon pack selection (SPEC F130.4, STORY-337, PLAN T303) — the admin chrome's third
        // swappable layer, the SAME SettingKind.Choice shape Station:Theme established immediately
        // above. The Choices carried HERE is an empty placeholder ONLY (there is no shipped icon
        // pack — F130.1's document has no "house" pack row, house icons are the shipped React
        // components an empty value falls back to) — SettingsController re-sources the live set from
        // IIconPackStore.GetAllSlugsAsync via IconPackChoices(IReadOnlyList<string>) instead (review
        // finding F2 — the slug-only projection, never the full pack row's own definition text), so a
        // pack installed or uninstalled after boot widens/narrows what is selectable with no restart,
        // the same live-resolution split Station:Theme already established. Live: IconPackController.Active
        // reads it via IOptionsMonitor<StationOptions> per request. Default "" resolves to house
        // icons (F130.4) and is ALWAYS a selectable choice in its own right (review finding F1 —
        // IconPackChoices carries it first, flagged IsDefault, on every station regardless of how many
        // packs are installed); a value naming an uninstalled pack (the F130.5 fail-open uninstall —
        // DELETE never touches this setting) resolves the same way, never an error.
        new("Station:IconPack",                               SettingApplyMode.Live,          SettingKind.Choice,     "", []),

        // The F107 context seam (SPEC F107.2/F107.7, F108.1-F108.2, F109.1, STORY-297, PLAN T226) —
        // Context:{Key}:* per registered IContextProvider (weather, history today; any future
        // provider joins with the identical four-key shape, no allowlist change needed beyond its
        // own row). Read live via ConfigurationContextSettingsProvider, so a PUT here reaches the
        // very next cadence-slot tick with no api restart. Enabled off by default (fail-closed,
        // F107.2/F108.1); SegmentCadenceMinutes/PatterCadenceMinutes/PersonaId all default to the
        // NoOp answer (60/0/null) when unset — see ContextProviderSettings' own remarks for why
        // null/0/negative PersonaId all mean "the on-air DJ".
        new("Context:Weather:Enabled",                        SettingApplyMode.Live,          SettingKind.Boolean,    ""),
        // F108.2's segment-cadence floor (30 minutes, "the ruled hard max of twice an hour") is
        // enforced HERE at write time — SettingValidator's own weather-specific range (F2/F4 fix,
        // T226 review) — the operator-facing rule; WeatherContextProvider's own
        // ICadenceFlooredContextProvider capability, consulted directly by ContextPipeline, is the
        // structural backstop for a value that reaches it some other way (an appsettings/env
        // override, which never passes through this validator at all).
        new("Context:Weather:SegmentCadenceMinutes",          SettingApplyMode.Live,          SettingKind.Number,     "minutes"),
        new("Context:Weather:PatterCadenceMinutes",           SettingApplyMode.Live,          SettingKind.Number,     "minutes"),
        new("Context:Weather:PersonaId",                      SettingApplyMode.Live,          SettingKind.Number,     ""),
        new("Context:History:Enabled",                        SettingApplyMode.Live,          SettingKind.Boolean,    ""),
        new("Context:History:SegmentCadenceMinutes",          SettingApplyMode.Live,          SettingKind.Number,     "minutes"),
        new("Context:History:PatterCadenceMinutes",           SettingApplyMode.Live,          SettingKind.Number,     "minutes"),
        new("Context:History:PersonaId",                      SettingApplyMode.Live,          SettingKind.Number,     ""),

        // Station broadcast location (SPEC F108.1, F108.3, PLAN T226) — read live through
        // IStationLocationProvider by WeatherContextProvider, so a PUT here reaches the very next
        // fetch with no api restart. Latitude/Longitude are raw strings, deliberately unvalidated
        // (StationLocation's own remarks: "blank or invalid" is each coordinate-consuming provider's
        // own fail-closed check, not this validator's) — WeatherContextProvider degrades to "off"
        // rather than erroring on a bad value. SpokenName is the ONLY location string ever spoken
        // (F108.3); blank means no place name is ever spoken.
        new("Station:Location:Latitude",                      SettingApplyMode.Live,          SettingKind.String,     ""),
        new("Station:Location:Longitude",                     SettingApplyMode.Live,          SettingKind.String,     ""),
        new("Station:Location:SpokenName",                    SettingApplyMode.Live,          SettingKind.String,     ""),

        // Clock-anchored imaging knobs (SPEC F110.1/F110.3, gh-#381) — allowlisted now per PLAN
        // T226, read starting PLAN T230's top-of-hour producer (called by the same ContextTickerService
        // this task adds). Both off by default — mirrors Station:Audience's own T111 precedent (the
        // property + allowlist entry land before the first consumer).
        new("Station:Imaging:ClockAnchoredIdents",            SettingApplyMode.Live,          SettingKind.Boolean,    ""),
        new("Station:Imaging:TimeAnnouncements",              SettingApplyMode.Live,          SettingKind.Boolean,    ""),
        // TimeDate elapsed-due expiry budget (SPEC F124.4/F141.1, gh-#469/gh-#526, PLAN T269/T326) —
        // read live through IStationImagingSettingsProvider by SpeechDeferralQueue.TryDequeueDue's own
        // expiry check (Orchestrator forwards the value fresh once per unit), so a PUT here reaches the
        // very next drain with no api restart. Defaults to 420 seconds (SPEC F141.1 — widened from the
        // original 300s/5-minute budget once gh-#526's field data showed every real overrun landing
        // just past it); StationId (idents) are exempt by design — this budget governs TimeDate only.
        // Seconds, not minutes (F141.1's own unit change, replacing the prior TimeAnnouncementStaleMinutes
        // key outright): a drain inside this budget but more than 90 seconds past the armed hour speaks
        // the honest "just past" variant instead of the classic line (Orchestrator's own remarks).
        new("Station:Imaging:TimeAnnouncementBudgetSeconds",  SettingApplyMode.Live,          SettingKind.Number,     "seconds"),

        // Show-flavor patter line (SPEC F116.3, STORY-308, PLAN T249) — an ordinary LeadIn/BackAnnounce
        // break during a show may carry the show's flavor as spoken color, sharing F107.5's own single
        // extra-line slot with the context-fact patter lane (context always wins when both are due).
        // Read live through IShowPatterCadenceProvider by GenWave.Orchestration.ShowFlavorLineGate, so
        // a PUT here reaches the very next eligible break with no api restart. 0 (the default) disables
        // it entirely — an opt-in feature, not a default-on one (mirrors Context:{Key}:PatterCadenceMinutes's
        // own "0 = off" floor immediately above).
        new("Station:Shows:PatterCadenceMinutes",             SettingApplyMode.Live,          SettingKind.Number,     "minutes"),

        // Crosstalk two-voice banter, the duration-fit knob (SPEC F127.4, STORY-326, PLAN T282) —
        // CrosstalkScriptWriter (GenWave.Tts) reads this fresh via IOptionsMonitor<CrosstalkOptions>
        // on every generation attempt, so a PUT here reaches the very next attempt with no api
        // restart. Defaults to the ratified 50s (PLAN T333 amendment); an estimate over target
        // discards the WHOLE exchange rather than trimming a line (F127.4 — a cut dialogue line
        // breaks the reaction to it).
        new("Crosstalk:DurationTargetSeconds",                SettingApplyMode.Live,          SettingKind.Number,     "seconds"),
        // Crosstalk scope/cadence (SPEC F127.8, STORY-328, PLAN T285) — Shows is a JSON array of
        // enabled show SLUGS, never display names (T175's "names slugs, not labels" rule — a rename
        // must never silently kill banter forever; the Station:Envelope:Genres opaque-string-kind
        // shape otherwise); EMPTY means the feature is OFF everywhere (fail-closed — no station's
        // sound changes on upgrade). Read live through ICrosstalkScopeProvider by
        // GenWave.Orchestration.CrosstalkPlanner, so a PUT here reaches the very next
        // casting/eligibility check with no api restart. EveryNthAiring defaults to 1 (every eligible
        // airing carries banter) — the counting itself is a LATER task's own concern (PLAN T287's
        // vend gate).
        new("Crosstalk:Shows",                                SettingApplyMode.Live,          SettingKind.String,     ""),
        new("Crosstalk:EveryNthAiring",                       SettingApplyMode.Live,          SettingKind.Number,     "airings"),
    };

    /// <summary>All operator-editable settings, keyed by configuration key.</summary>
    public static readonly IReadOnlyDictionary<string, AllowedSetting> ByKey =
        All.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);
}
