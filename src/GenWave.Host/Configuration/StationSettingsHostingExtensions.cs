using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Host.Announcements;
using GenWave.Host.Options;
using GenWave.MediaLibrary.Station;
using GenWave.Orchestration;

namespace GenWave.Host.Configuration;

/// <summary>
/// Wires everything that rides <c>ConnectionStrings:Station</c> (the <c>station_svc</c> role):
/// the settings overlay, the settings store/validator, and the persona store + live accessor.
/// </summary>
static class StationSettingsHostingExtensions
{
    /// <summary>
    /// Boot-time marker (gh-#412) a deliberately DB-free host sets to <c>true</c> — via plain
    /// configuration, e.g. <c>UseSetting</c> — to declare that the absent Station connection string
    /// is intentional, so the overlay provider skips its "no Station connection string" stderr
    /// diagnostic. Set by the SEAMS.md generator's composition snapshot
    /// (<c>SeamCompositionSnapshot</c> in GenWave.Host.Tests); a real deploy never sets it, keeping
    /// an accidentally empty connection string observable.
    /// </summary>
    public const string ExpectNoStoreKey = "Station:Settings:ExpectNoStore";

    public static WebApplicationBuilder AddGenWaveStationSettings(this WebApplicationBuilder builder)
    {
        var stationConnStr = builder.Configuration.GetConnectionString("Station") ?? string.Empty;
        var expectNoStore = builder.Configuration.GetValue<bool>(ExpectNoStoreKey);

        // ── Station settings overlay (STORY-042, Epic I) ────────────────────
        // The custom provider is registered AFTER env/appsettings so a row in station.settings wins
        // over file/env defaults. The source is created here (before builder.Build) so we can
        // register the singleton store against the same source instance — the store calls
        // source.BuiltProvider.Reload() after each write, which raises the change token for
        // IOptionsMonitor<T>.
        var stationSettingsSource = new StationSettingsConfigurationSource(stationConnStr, expectNoStore);
        builder.Configuration.AddEnvironmentVariables();   // ensure env vars are loaded before we append
        builder.Configuration.Sources.Add(stationSettingsSource);

        // Station settings store — singleton; same source the provider was built from so writes
        // can signal the live-reload change token. Factory (not instance) registration so the
        // store picks up whatever IStationEventSink binding wins once ALL extensions have run —
        // this extension executes first, before the sink is bound (gitea-#246).
        builder.Services.AddSingleton<IStationSettingsStore>(sp =>
            new StationSettingsStore(
                stationConnStr,
                stationSettingsSource,
                sp.GetRequiredService<IStationEventSink>(),
                sp.GetRequiredService<ILogger<StationSettingsStore>>()));

        // Settings validator — stateless, singleton.  Used by SettingsController.
        builder.Services.AddSingleton<SettingValidator>();

        // ── Persona store (SPEC F35.1/F35.4, STORY-120) ─────────────────────
        // Same ConnectionStrings:Station value as the settings overlay above — a dedicated
        // NpgsqlDataSource for station.persona (station_svc role), built lazily inside
        // AddPersonaStore so an empty/dev-mode connection string never blocks boot (mirrors
        // AddMediaLibrary's own lazy data-source factory); the failure only surfaces if a request
        // actually resolves IPersonaStore.
        builder.Services.AddPersonaStore(stationConnStr);

        // Persona memory store (SPEC F71.4-F71.6, STORY-194) — same station_svc connection string,
        // same lazy-data-source story as AddPersonaStore just above. STORY-194 shipped this
        // registration deliberately without a Host call site ("no consumer lands with this seam");
        // the card-export route (SPEC F79.1, STORY-208, PLAN T66) is that first consumer, reading
        // authored persona_memory rows for a card's lore[].
        builder.Services.AddPersonaMemoryStore(stationConnStr, builder.Configuration);

        // Persona taste store (SPEC F82.1, F84.1-F84.3; STORY-213, PLAN T64) — same station_svc
        // connection string, same lazy-data-source story as AddPersonaStore just above. T59 shipped
        // this registration deliberately without a Host call site ("the ranker (T63) and card
        // import (T66-T69) are the first consumers"); this is that first call site. IPersonaTasteReader
        // is the narrower read-only seam PersonaRanker actually depends on (F84.2 structural) — bound
        // against the SAME singleton instance, so there is exactly one IPersonaTasteStore in the
        // container regardless of which seam a consumer asks for.
        builder.Services.AddPersonaTasteStore(stationConnStr);
        builder.Services.AddSingleton<IPersonaTasteReader>(sp => sp.GetRequiredService<IPersonaTasteStore>());

        // Persona taste ACCRUAL store (SPEC F84.1-F84.6; STORY-215, PLAN T70) — same station_svc
        // connection string. Deliberately its own registration/interface, never widening
        // IPersonaTasteStore/IPersonaTasteReader: BoothLogController.ThumbTaste is its only consumer.
        builder.Services.AddPersonaTasteAccrualStore(stationConnStr);

        // Persona import store (SPEC F79.3, F79.6; STORY-209, PLAN T67) — same station_svc
        // connection string, own lazy data source (see AddPersonaImportStore's own remarks). The
        // import route (PersonaController.Import) is its only consumer.
        builder.Services.AddPersonaImportStore(stationConnStr);

        // Format-clock schedule store + resolver (SPEC F91.1, F91.3; STORY-240/241, PLAN T118-T120) —
        // same station_svc connection string as every registration above; station.segment_schedule
        // lives in the same schema. ScheduleRepository ships dark since T118 (AddScheduleStore itself
        // registers no consumer); this is the first Host call site.
        builder.Services.AddScheduleStore(stationConnStr);

        // Dated-specials store (SPEC F120.1, STORY-317, PLAN T258-T260) — same station_svc connection
        // string as every registration above; station.schedule_special lives in the same schema.
        // SpecialsRepository shipped dark at T258; SpecialsController (PLAN T259) made this store's
        // FIRST Host call site (author/list/delete a special through the Admin UI). CachingScheduleResolver
        // just below is the SECOND — and the one that makes a written special actually shadow the
        // weekly grid live, on the production feeder tick, rather than remain authoring-surface-only
        // (see that type's own class remarks for the caching/invalidation design).
        builder.Services.AddScheduleSpecialStore(stationConnStr);

        // CachingScheduleResolver MUST be a singleton — its constructor subscribes to
        // IScheduleStore.WeekChanged, (PLAN T260) IScheduleSpecialStore.SpecialsChanged, and (PLAN T360
        // review HIGH-1) IShowStore.ShowChanged, never unsubscribing from any of the three (no
        // IDisposable), so a scoped/transient registration would leak one subscription (and all three
        // wrapped store references) per instance created (T119 review F6, T119's own class remarks).
        // IShowStore is resolved here via plain constructor injection (the optional showStore
        // parameter's own default is never reached in production — AddShowStore below registers a real
        // one regardless of registration ORDER, since DI resolves constructor parameters lazily at
        // first use, long after every Add* call in this method has already run). ScheduleResolver
        // itself is a pure (snapshot, specials, wall clock) function with no state of its own — plain
        // AddSingleton is enough, no lifetime hazard either way.
        builder.Services.AddSingleton<ScheduleResolver>();
        builder.Services.AddSingleton<CachingScheduleResolver>();

        // OnAirPersonaAccessor (SPEC F35.2, F35.5, F91.5; STORY-241/242, PLAN T120): the ONE seam the
        // Orchestrator and the preview/status endpoints read the live on-air persona through — now
        // re-backed by CachingScheduleResolver instead of Station:Persona:ActiveId (retired, F91.5).
        // Never throws (WARN + null on any miss) — same contract the retired ActivePersonaAccessor
        // carried.
        builder.Services.AddSingleton<IActivePersonaAccessor, OnAirPersonaAccessor>();

        // Booth log (SPEC F72.1-F72.3, STORY-195): same station_svc connection string as the
        // settings overlay/persona store above — station.booth_log lives in the same schema. Adds
        // the store (IBoothLogAppender/IBoothLogReader), the queue, and the drain hosted service;
        // AddGenWavePlayout composes BoothLogWriter (IBoothLogEventConsumer) into the host's ONE
        // IStationEventSink binding.
        builder.Services.AddBoothLog(stationConnStr, builder.Configuration);

        // Request store (SPEC F87, STORY-224, PLAN T86): same station_svc connection string as
        // every other registration above — station.request lives in the same schema. Read directly
        // from configuration here (`.Get<RequestsOptions>()`, not IOptions<RequestsOptions>):
        // GenWave.MediaLibrary cannot reference GenWave.Host's options types, and this runs before
        // the DI container is built, so RequestRepository's ctor takes WishRetentionHours as a
        // plain already-resolved value instead (see RequestRepository's own remarks). The separate
        // `.AddOptions<RequestsOptions>()...ValidateOnStart()` call in Program.cs is what a future
        // T87/T88 IOptionsMonitor<RequestsOptions> consumer (WishMaxLength, the throttle knobs)
        // reads live from — this line only needs the one field early.
        var requestsOptions = builder.Configuration.GetSection(RequestsOptions.Section).Get<RequestsOptions>()
            ?? new RequestsOptions();
        builder.Services.AddRequestStore(stationConnStr, requestsOptions.WishRetentionHours);

        // Owner theme store (SPEC F103.7, STORY-271, PLAN T181) — same station_svc connection
        // string as every registration above; station.theme lives in the same schema.
        // ThemeRepository ships dark since T181 (AddThemeStore itself registers no consumer):
        // ThemeCatalog (T182) and the theme import route (T184) are the first Host call sites.
        builder.Services.AddThemeStore(stationConnStr);

        // Font pack store (SPEC F104, STORY-282, PLAN T198) — same station_svc connection string as
        // every registration above; station.font_pack(+_face) lives in the same schema.
        // FontPackRepository shipped dark at T198 (AddFontPackStore itself registered no consumer):
        // FontPackController's install route (PLAN T199) is the first Host call site.
        builder.Services.AddFontPackStore(stationConnStr);

        // Show store (SPEC F115.1, STORY-305, PLAN T239) — same station_svc connection string as
        // every registration above; station.show lives in the same schema. ShowRepository shipped
        // dark at T239 (AddShowStore itself registers no consumer): ShowsController (PLAN T240) is
        // the first Host call site.
        builder.Services.AddShowStore(stationConnStr);

        // The visual layer's four stores (SPEC F128-F131, STORY-332/333/337/339, PLAN T290) — same
        // station_svc connection string as every registration above; station.persona_avatar,
        // station.avatar_pack(+_item), station.icon_pack, and station.station_image all live in the
        // same schema. Every one of these four repositories ships dark at T290 (none of these Add*
        // calls registers a consumer): PersonaAvatarController (T295), AvatarPackController (T293),
        // IconPackController (T303), and StationImageController (T307) are their respective first
        // Host call sites — the same "seam before consumer" way station.theme (T181) and
        // station.font_pack (T198) shipped just above.
        builder.Services.AddPersonaAvatarStore(stationConnStr);
        builder.Services.AddAvatarPackStore(stationConnStr);
        builder.Services.AddIconPackStore(stationConnStr);
        builder.Services.AddStationImageStore(stationConnStr);

        // Announcement store (SPEC F143, STORY-357, PLAN T337) — same station_svc connection string
        // as every registration above; station.announcement lives in the same schema.
        // AnnouncementRepository shipped dark at T337, keyed on its own concrete type (no seam yet);
        // PLAN T339 gave it its first Host call site (AnnouncementsController) AND its first seam
        // (IAnnouncementStore) in the same task — see AnnouncementServiceCollectionExtensions' own
        // remarks for why this registration now keys on the interface like every sibling above.
        //
        // The vend-only IAnnouncementSource seam (SPEC F144.1, PLAN T341) is registered in the SAME
        // call (T341 review finding F9 — folded from a separate AddAnnouncementSource call that had
        // to run AFTER this one, a call-order hazard nothing enforced), wrapped with the SPEC F145.2
        // SpectatorMode refusal here, at the ONLY layer that is allowed to read Station:SpectatorMode:
        // neither AnnouncementRepository (MediaLibrary) nor Orchestrator (GenWave.Orchestration) ever
        // sees it. Mirrors PlayoutServiceCollectionExtensions' own MediaExistencePushGuard wrap
        // (gh-#612) one project over — see SpectatorModeAnnouncementVendGuard's own remarks.
        builder.Services.AddAnnouncementStore(
            stationConnStr,
            (inner, sp) => new SpectatorModeAnnouncementVendGuard(
                inner, sp.GetRequiredService<IOptionsMonitor<StationOptions>>()));

        return builder;
    }
}
