using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Configuration;
using GenWave.Host.Options;
using GenWave.Tts;

namespace GenWave.Host.Seeding;

/// <summary>
/// One-shot idempotent safe-loop boot seed (SPEC F27.6, STORY-080). Fresh boot: create library
/// <c>"safe"</c> if absent, render a voice-only segment from <c>Station:Safe:SeedMessage</c> into it
/// through <see cref="ISafeSegmentAuthor"/> — the identical all-or-nothing pipeline
/// <c>POST /api/safe-segments</c> (STORY-079) already ships — then, iff no operator
/// <c>Station:SafeScope:LibraryIds</c> value exists in the settings store, point the SafeScope
/// overlay at the seeded library. The marker is written only when every step above succeeded.
///
/// Any failure degrades to a WARN and <see cref="SafeLoopSeedOutcome.Failed"/> — it never throws out
/// of <see cref="SeedAsync"/> (except <see cref="OperationCanceledException"/> from a genuine host
/// shutdown) — so a bad Kokoro/DB day never blocks the host from starting; the next boot retries.
///
/// Deliberately a plain class, not <see cref="Microsoft.Extensions.Hosting.IHostedService"/> itself —
/// <see cref="SafeLoopSeedHostedService"/> is the thin one-shot scheduling shell, so
/// <see cref="SeedAsync"/> can be exercised directly against fakes in tests with no hosted-service
/// lifecycle plumbing.
/// </summary>
public sealed class SafeLoopSeeder(
    ISafeLoopSeedMarkerStore markerStore,
    ILibraryRepository libraryRepository,
    IAdminLibraryWrite libraryWriter,
    ISafeSegmentAuthor author,
    IStationSettingsStore settingsStore,
    IOptionsMonitor<StationOptions> stationMonitor,
    ILogger<SafeLoopSeeder> logger)
{
    /// <summary>Name of the library the seed creates or reuses (F27.6 step a).</summary>
    public const string SafeLibraryName = "safe";

    /// <summary>
    /// Distinguishes the boot-seeded row from one an operator authors manually through
    /// <c>POST /api/safe-segments</c> — the latter still defaults to the plain
    /// <see cref="SafeSegmentAuthor.DefaultTitle"/> (SPEC F29.3, STORY-096, gitea-#185).
    /// </summary>
    public const string SeedTitle = "Please Stand By (Station Default)";

    // The overlay key IS on StationSettingsAllowlist (K4/F19) — writing it through
    // IStationSettingsStore.WriteAsync raises the same reload token PUT /api/settings does, so
    // IOptionsMonitor<StationOptions> re-binds without an api restart.
    const string SafeScopeKey = "Station:SafeScope:LibraryIds";

    public async Task<SafeLoopSeedOutcome> SeedAsync(CancellationToken ct)
    {
        try
        {
            if (await markerStore.ExistsAsync(ct))
                return SafeLoopSeedOutcome.AlreadySeeded;

            var library = await EnsureSafeLibraryAsync(ct);

            // A library found with content already in it means a prior attempt rendered the row but
            // failed before the overlay/marker step (or an operator populated it) — reuse it rather
            // than rendering a second "Please Stand By" row (F27.6: retries must not duplicate).
            if (library.MediaCount > 0)
            {
                logger.LogInformation(
                    "Boot seed: safe library (id={LibraryId}) already holds {MediaCount} row(s) — reusing, skipping render",
                    library.Id, library.MediaCount);
            }
            else
            {
                var result = await RenderSeedSegmentAsync(library.Id, ct);

                if (!result.Succeeded)
                {
                    logger.LogWarning(
                        "Boot seed: safe-segment render failed reason={FailureReason} detail={FailureDetail} " +
                        "— host starting normally, will retry on next boot",
                        result.FailureReason, result.FailureDetail);
                    return SafeLoopSeedOutcome.Failed;
                }

                logger.LogInformation(
                    "Boot seed: safe library ready (id={LibraryId}); seeded segment media id={MediaId}",
                    library.Id, result.MediaId);
            }

            await WriteSafeScopeOverlayIfAbsentAsync(library.Id, ct);
            await markerStore.MarkCompletedAsync(ct);

            return SafeLoopSeedOutcome.Seeded;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Boot seed: safe-loop seed failed — host starting normally, will retry on next boot");
            return SafeLoopSeedOutcome.Failed;
        }
    }

    /// <summary>
    /// Looks the library up by name first (F27.6: "create if absent"); only attempts a create when no
    /// row is found. A <see cref="LibraryWriteResult.NameConflict"/> on that create (e.g. an operator's
    /// own <c>POST /api/libraries</c> racing the boot seed) is benign — re-look-up and reuse rather
    /// than fail. Returns the full <see cref="LibraryAdminInfo"/> (not just the id) so the caller can
    /// see <see cref="LibraryAdminInfo.MediaCount"/> and decide whether a render is even needed.
    /// </summary>
    async Task<LibraryAdminInfo> EnsureSafeLibraryAsync(CancellationToken ct)
    {
        var existing = await FindSafeLibraryAsync(ct);
        if (existing is not null)
            return existing;

        var created = await libraryWriter.CreateAsync(SafeLibraryName, ct);
        if (created is LibraryWriteResult.Created ok)
            return new LibraryAdminInfo(ok.Id, SafeLibraryName, MediaCount: 0);

        if (created is LibraryWriteResult.NameConflict)
        {
            var afterRace = await FindSafeLibraryAsync(ct);
            if (afterRace is not null)
                return afterRace;
        }

        throw new InvalidOperationException(
            $"Could not create or find library \"{SafeLibraryName}\" (create result: {created}).");
    }

    async Task<LibraryAdminInfo?> FindSafeLibraryAsync(CancellationToken ct)
    {
        var libraries = await libraryRepository.GetAllWithMediaCountAsync(ct);
        return libraries.FirstOrDefault(
            l => string.Equals(l.Name, SafeLibraryName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Builds the request exactly as <c>SafeSegmentsController</c> does for the operator-facing
    /// endpoint (P6): station-scoped values resolved here, <see cref="StationSafeOptions.SeedMessage"/>
    /// passed through RAW (<c>{StationName}</c> expansion is <see cref="SafeSegmentAuthor"/>'s job
    /// alone, SPEC F29.1–F29.2, STORY-095 — this seeder no longer pre-expands), voice left null so
    /// <see cref="SafeSegmentAuthor"/> applies its own default (<c>Station:Voice</c>), and an explicit
    /// <see cref="SeedTitle"/> so the boot-seeded row reads distinctly from a manually authored one
    /// (SPEC F29.3, STORY-096).
    /// </summary>
    async Task<SafeSegmentAuthorResult> RenderSeedSegmentAsync(long libraryId, CancellationToken ct)
    {
        var station = stationMonitor.CurrentValue;
        var safe = station.Safe;

        var request = new SafeSegmentRequest(
            Text: safe.SeedMessage,
            LibraryId: libraryId,
            StationName: station.Name,
            DefaultVoice: station.Voice,
            AuthoredRoot: safe.AuthoredRoot,
            BedDuckDb: safe.BedDuckDb,
            BedPadSeconds: safe.BedPadSeconds,
            Title: SeedTitle);

        return await author.AuthorAsync(request, ct);
    }

    /// <summary>
    /// Writes the SafeScope overlay iff <c>station.settings</c> holds no operator row for
    /// <see cref="SafeScopeKey"/> — checked via the store directly (not the merged
    /// <c>IOptionsMonitor</c> value, which always shows the appsettings default of <c>[1]</c>, F21.8).
    /// An operator row present with an EMPTY array still counts as "exists" (a deliberate F25 choice)
    /// and is left untouched.
    /// The read-then-write here is not atomic (an operator's own PUT could race between the two), but
    /// that window only exists on the one first successful boot before the marker is set — an
    /// acceptable, vanishingly narrow risk for a fresh-deploy convenience, not a steady-state hazard.
    /// </summary>
    async Task WriteSafeScopeOverlayIfAbsentAsync(long libraryId, CancellationToken ct)
    {
        var overrides = await settingsStore.ReadAllAsync(ct);
        if (overrides.ContainsKey(SafeScopeKey))
        {
            logger.LogInformation(
                "Boot seed: operator SafeScope override already present — leaving it untouched (F27.6)");
            return;
        }

        await settingsStore.WriteAsync(SafeScopeKey, new long[] { libraryId }, ct);
        logger.LogInformation("Boot seed: SafeScope overlay set to [{LibraryId}]", libraryId);
    }
}
