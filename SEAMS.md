# SEAMS.md

> **Generated. Never hand-edit.** Produced by `tools/SeamIndexGenerator` from the ACTUAL
> DI registrations GenWave.Host's composition root (`Program.cs`) builds — every seam below
> was resolved from a live `IServiceCollection`/`IServiceProvider`
> (`WebApplicationFactory<Program>`, no Kestrel, no Postgres/Liquidsoap/Kokoro/Ollama/Icecast
> reached), never re-typed by hand. Regenerate: `dotnet run --project tools/SeamIndexGenerator`.
> Regenerated and byte-diffed by CI (SPEC F105.6, `tools/check-seam-index.sh`) — a new or
> changed seam shipped without a regenerated index is a red check.
>
> **Check this file before adding a seam** — extend or decorate an existing port before
> minting a near-duplicate.
>
> **Scope & method.** One row per GenWave.* interface port the composition root registers —
> port → default adapter → binding site, grouped by section below. "Binding site" is the
> PROJECT that owns the effective (last-registered) adapter, not the specific `Add*` call:
> nothing on a `ServiceDescriptor` records which extension method added it, so per-registration
> attribution is impractical — this generator attributes honestly at project granularity
> instead of guessing. The Adapter/Lifetime columns are always the port's LAST registration —
> what a plain `IServiceProvider.GetService<T>()` call actually returns. A port registered
> more than once also lists every earlier registration in its Notes column, honestly labeled
> "also registered" rather than "overridden": nothing on a `ServiceDescriptor` records whether
> a later registration is a `TryAdd`-default override (single-resolve wins, e.g.
> `IPersonaPickProvider`) or one leg of a fan-out consumed via `IEnumerable<T>`/`GetServices<T>()`
> where every registration stays active (e.g. `IDependencyProbe`'s three health probes) — read
> the call site to tell which.
>
> **Decorators.** Notes also lists "wraps: ..." where a decorator chain is mechanically
> derivable — a constructor parameter typed as a CONCRETE class implementing the same port
> (e.g. `DegradationGatedCopyWriter`'s `ISegmentCopyWriter` wraps both `LlmCopyWriter` and
> `TemplateCopyWriter` directly). It is NOT always derivable: `ITtsSynthesizer` is a real,
> three-deep chain (`NormalizingTtsSynthesizer` wraps `FallbackTtsSynthesizer` wraps
> Kokoro/Piper) that this generator cannot see past — every hop there is an INTERFACE-typed
> constructor parameter (`ITtsSynthesizer inner`/`primary`) whose actual concrete argument is
> chosen inside a hand-written factory closure, not reflectable metadata. `ITtsVoiceLister`
> (`CachedVoiceLister` wrapping Kokoro) has the identical shape. A row with no "wraps:" note
> may still be layered — read `TtsServiceCollectionExtensions.cs`'s own registration comments
> (and its siblings) for the ground truth a generator this size cannot fully mechanize.
>
> Enumerated under this repo's `Development` environment defaults
> (`appsettings.Development.json`) plus a placeholder `ConnectionStrings:Library` — the same
> minimal, DB-free config `GenWave.Host.Tests` already proves is enough for the composition
> root to build cleanly. Program.cs registers its whole graph unconditionally (no
> environment- or flag-gated `Add*` branch exists today), so nothing is known to be missing
> from this map for that reason.
>
> **71 seams across 5 projects.**

## GenWave.Host (17 seams)

| Port | Adapter | Lifetime | Notes |
|---|---|---|---|
| `GenWave.Core.Abstractions.IAudiencePostureProvider` | `GenWave.Host.Options.OptionsMonitorAudiencePostureProvider` | Singleton | — |
| `GenWave.Core.Abstractions.IBoundaryBiasProvider` | `GenWave.Host.Options.OptionsMonitorBoundaryBiasProvider` | Singleton | — |
| `GenWave.Core.Abstractions.ICadenceProvider` | `GenWave.Host.Options.OptionsMonitorCadenceProvider` | Singleton | — |
| `GenWave.Core.Abstractions.ILiquidsoapControl` | `GenWave.Host.Engine.LiquidsoapControl` | Singleton | — |
| `GenWave.Core.Abstractions.IListenerStatsSource` | `GenWave.Host.Stats.IcecastListenerStatsSource` | Singleton | — |
| `GenWave.Core.Abstractions.ILlmBatchGate` | `GenWave.Host.Enrichment.LlmBatchGate` | Singleton | — |
| `GenWave.Core.Abstractions.IRenderBudgetProvider` | `GenWave.Host.Options.OptionsMonitorRenderBudgetProvider` | Singleton | — |
| `GenWave.Core.Abstractions.IRequestOverrideEnvelopeProvider` | `GenWave.Host.Options.OptionsMonitorRequestOverrideEnvelopeProvider` | Singleton | — |
| `GenWave.Core.Abstractions.IRotationSettingsProvider` | `GenWave.Host.Options.OptionsMonitorRotationSettingsProvider` | Singleton | — |
| `GenWave.Core.Abstractions.ISafeScopeProvider` | `GenWave.Host.Options.OptionsMonitorSafeScopeProvider` | Singleton | — |
| `GenWave.Core.Abstractions.IStationClockProvider` | `GenWave.Host.Options.OptionsMonitorStationClockProvider` | Singleton | — |
| `GenWave.Core.Abstractions.IStationDefaultEnvelopeSource` | `GenWave.Host.Options.OptionsMonitorStationDefaultEnvelopeSource` | Singleton | — |
| `GenWave.Core.Abstractions.IStationEventSink` | `GenWave.Host.Playout.CompositeStationEventSink` | Singleton | also registered: `GenWave.Core.Events.NoOpStationEventSink` (GenWave.Abstractions) |
| `GenWave.Core.Abstractions.IStationIdentityProvider` | `GenWave.Host.Options.OptionsMonitorStationIdentityProvider` | Singleton | — |
| `GenWave.Core.Abstractions.IStationScopeProvider` | `GenWave.Host.Options.OptionsMonitorStationScopeProvider` | Singleton | — |
| `GenWave.Host.Configuration.IStationSettingsStore` | `GenWave.Host.Configuration.StationSettingsStore` | Singleton | — |
| `GenWave.Host.Seeding.ISafeLoopSeedMarkerStore` | `GenWave.Host.Seeding.SafeLoopSeedMarkerStore` | Singleton | — |

## GenWave.Loudness (5 seams)

| Port | Adapter | Lifetime | Notes |
|---|---|---|---|
| `GenWave.Core.Abstractions.IAudioMixer` | `GenWave.Loudness.FfmpegAudioMixer` | Singleton | — |
| `GenWave.Core.Abstractions.IBpmAnalyzer` | `GenWave.Loudness.AubioBpmAnalyzer` | Singleton | — |
| `GenWave.Core.Abstractions.ICueAnalyzer` | `GenWave.Loudness.FfmpegCueAnalyzer` | Singleton | — |
| `GenWave.Core.Abstractions.IEnergyAnalyzer` | `GenWave.Loudness.FfmpegEnergyAnalyzer` | Singleton | — |
| `GenWave.Core.Abstractions.ILoudnessAnalyzer` | `GenWave.Loudness.FfmpegLoudnessAnalyzer` | Singleton | — |

## GenWave.MediaLibrary (30 seams)

| Port | Adapter | Lifetime | Notes |
|---|---|---|---|
| `GenWave.Core.Abstractions.IAdminLibraryWrite` | `GenWave.MediaLibrary.Catalog.AdminLibraryRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IAdminMediaLookup` | `GenWave.MediaLibrary.Catalog.MediaRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IAdminMediaQuery` | `GenWave.MediaLibrary.Catalog.MediaRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IAdminMediaReenrichment` | `GenWave.MediaLibrary.Catalog.MediaRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IAdminMediaWrite` | `GenWave.MediaLibrary.Catalog.MediaRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IArtworkTokenStore` | `GenWave.MediaLibrary.Catalog.ArtworkTokenRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IAuthoredCatalogWriter` | `GenWave.MediaLibrary.Catalog.MediaRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IBoothLogAppender` | `GenWave.MediaLibrary.Station.BoothLogRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IBoothLogEventConsumer` | `GenWave.MediaLibrary.Station.BoothLogWriter` | Singleton | — |
| `GenWave.Core.Abstractions.IBoothLogReader` | `GenWave.MediaLibrary.Station.BoothLogRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IExplicitClassifier` | `GenWave.MediaLibrary.ExplicitClassification.OllamaExplicitClassifier` | Singleton | — |
| `GenWave.Core.Abstractions.IFontPackStore` | `GenWave.MediaLibrary.Station.FontPackRepository` | Singleton | — |
| `GenWave.Core.Abstractions.ILibraryRepository` | `GenWave.MediaLibrary.Catalog.LibraryRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IMediaCatalog` | `GenWave.MediaLibrary.Catalog.MediaRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IMediaExplicitOverride` | `GenWave.MediaLibrary.Catalog.MediaRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IMediaLibraryMembership` | `GenWave.MediaLibrary.Catalog.MediaLibraryMembershipRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IMediaPurge` | `GenWave.MediaLibrary.Catalog.MediaRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IMediaRating` | `GenWave.MediaLibrary.Catalog.MediaRatingRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IMoodTagger` | `GenWave.MediaLibrary.Mood.OllamaMoodTagger` | Singleton | — |
| `GenWave.Core.Abstractions.IPersonaImportStore` | `GenWave.MediaLibrary.Station.PersonaImportRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IPersonaMemory` | `GenWave.MediaLibrary.Station.PersonaMemoryRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IPersonaStore` | `GenWave.MediaLibrary.Station.PersonaRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IPersonaTasteAccrualStore` | `GenWave.MediaLibrary.Station.PersonaTasteAccrualRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IPersonaTasteReader` | `GenWave.MediaLibrary.Station.PersonaTasteRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IPersonaTasteStore` | `GenWave.MediaLibrary.Station.PersonaTasteRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IRequestCatalogProbe` | `GenWave.MediaLibrary.Catalog.RequestCatalogProbeRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IRequestStore` | `GenWave.MediaLibrary.Station.RequestRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IScheduleStore` | `GenWave.MediaLibrary.Station.ScheduleRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IThemeStore` | `GenWave.MediaLibrary.Station.ThemeRepository` | Singleton | — |
| `GenWave.Core.Abstractions.IYearLookup` | `GenWave.MediaLibrary.YearLookup.MusicBrainzYearLookup` | Singleton | — |

## GenWave.Orchestration (7 seams)

| Port | Adapter | Lifetime | Notes |
|---|---|---|---|
| `GenWave.Core.Abstractions.IActivePersonaAccessor` | `GenWave.Orchestration.OnAirPersonaAccessor` | Singleton | — |
| `GenWave.Core.Abstractions.IEnvelopeProvider` | `GenWave.Orchestration.ScheduleEnvelopeProvider` | Singleton | — |
| `GenWave.Core.Abstractions.INextItemProvider` | `GenWave.Orchestration.Orchestrator` | Singleton | — |
| `GenWave.Core.Abstractions.IPatterDurationEstimator` | `GenWave.Orchestration.RollingPatterDurationEstimator` | Singleton | — |
| `GenWave.Orchestration.IPersonaPickProvider` | `GenWave.Orchestration.RankerPersonaPickProvider` | Singleton | also registered: `GenWave.Orchestration.NoOpPersonaPickProvider` (GenWave.Orchestration) |
| `GenWave.Orchestration.IRandomSource` | `GenWave.Orchestration.SystemRandomSource` | Singleton | — |
| `GenWave.Orchestration.IRequestFulfillmentSource` | `GenWave.Orchestration.RequestFulfillmentProvider` | Singleton | also registered: `GenWave.Orchestration.NoOpRequestFulfillmentSource` (GenWave.Orchestration) |

## GenWave.Tts (12 seams)

| Port | Adapter | Lifetime | Notes |
|---|---|---|---|
| `GenWave.Core.Abstractions.ICopyBoundsProvider` | `GenWave.Tts.OptionsMonitorCopyBoundsProvider` | Singleton | — |
| `GenWave.Core.Abstractions.IPersonaPreviewWriter` | `GenWave.Tts.LlmCopyWriter` | Singleton | — |
| `GenWave.Core.Abstractions.ISegmentCopyWriter` | `GenWave.Tts.DegradationGatedCopyWriter` | Singleton | wraps: `GenWave.Tts.LlmCopyWriter` (GenWave.Tts), `GenWave.Tts.TemplateCopyWriter` (GenWave.Tts) |
| `GenWave.Core.Abstractions.ITtsSegmentSource` | `GenWave.Tts.TtsSegmentSource` | Singleton | — |
| `GenWave.Core.Abstractions.ITtsSynthesizer` | `GenWave.Tts.NormalizingTtsSynthesizer` | Singleton | — |
| `GenWave.Core.Abstractions.ITtsVoiceLister` | `GenWave.Tts.CachedVoiceLister` | Singleton | — |
| `GenWave.Tts.IDegradationModeReader` | `GenWave.Tts.DegradationController` | Singleton | — |
| `GenWave.Tts.IDependencyHealth` | `GenWave.Tts.DependencyHealthStore` | Singleton | — |
| `GenWave.Tts.IDependencyProbe` | `GenWave.Tts.PiperHealthProbe` | Singleton | also registered: `GenWave.Tts.OllamaHealthProbe` (GenWave.Tts), `GenWave.Tts.KokoroHealthProbe` (GenWave.Tts) |
| `GenWave.Tts.IFallbackProfileRenderer` | `GenWave.Tts.KokoroFallbackRenderer` | Singleton | also registered: `GenWave.Tts.PiperTtsSynthesizer` (GenWave.Tts) |
| `GenWave.Tts.ISafeSegmentAuthor` | `GenWave.Tts.SafeSegmentAuthor` | Singleton | — |
| `GenWave.Tts.ISpeechNormalizationPreview` | `GenWave.Tts.NormalizingTtsSynthesizer` | Singleton | — |
