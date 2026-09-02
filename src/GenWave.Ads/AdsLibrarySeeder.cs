using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads;

/// <summary>
/// SPEC F159.1 (STORY-388, PLAN T396) — the marker-gated boot seed for the Ads library (the
/// <c>SafeLoopSeeder</c> pattern, <c>GenWave.Host.Seeding.SafeLoopSeeder</c>): creates a
/// <c>library.library</c> row named <see cref="AdsOptions.LibraryName"/> ("ads" by default) if
/// absent, reused if present — never re-created. Idempotent by construction: every call either finds
/// the row (no write) or creates it (one write); calling it twice in a row, or twice concurrently
/// across a restart race, never produces a second row (SafeLoopSeeder's own <c>NameConflict</c>-race
/// handling below).
///
/// <para>
/// <b>No render, no overlay, no persisted marker (a deliberate simplification of the SafeLoopSeeder
/// shape, not a shortcut).</b> <c>SafeLoopSeeder</c> needs a settings-table marker DISTINCT from its
/// library's mere existence because it is genuinely multi-step (create library → render a segment →
/// conditionally write a SafeScope overlay) — a library that exists with content already in it is
/// AMBIGUOUS there (did the WHOLE prior attempt succeed, or did it fail after rendering but before
/// the overlay?), so a separate "every step finished" marker is load-bearing. This seeder has exactly
/// ONE step — "does a library named <see cref="AdsOptions.LibraryName"/> exist; if not, create it" —
/// with no render and no overlay, so the library's own presence, found by name, already answers that
/// one question unambiguously: it IS the marker. Adding a second, settings-table-backed marker on top
/// would mean a new Postgres write path this project cannot own anyway (L2 confinement keeps
/// Npgsql/Dapper out of <c>GenWave.Ads</c> entirely — the marker table SafeLoopSeeder's own
/// <c>ISafeLoopSeedMarkerStore</c> talks to lives behind a Postgres-backed implementation in
/// <c>GenWave.Host</c>), purely to re-derive a fact <see cref="ILibraryRepository.GetAllWithMediaCountAsync"/>
/// already gives for free.
/// </para>
///
/// <para>
/// <b>A genuine divergence from SafeLoopSeeder, not just a simpler mechanism.</b> Because presence
/// IS the marker, an operator who deletes the <see cref="AdsOptions.LibraryName"/> library gets it
/// silently RECREATED (empty) on the very next boot — <c>SafeLoopSeeder</c>'s own settings-table
/// marker would instead leave a deleted safe library gone for good (the marker still reads
/// "completed", so nothing re-seeds it). That difference is deliberate, not an oversight: an empty
/// safe library is a real hazard (the never-silence floor needs a row to play), while an empty ads
/// library is harmless by construction — <see cref="LibraryAdSpotSource"/> just vends
/// <see langword="null"/> (F158.1's always-legal answer) until spots exist again. Recreate-on-delete
/// is the FRIENDLIER posture here, not a compromise.
/// </para>
///
/// <para>
/// Any failure degrades to a WARN and <see cref="AdsLibrarySeedOutcome.Failed"/> — it never throws
/// out of <see cref="SeedAsync"/> (except <see cref="OperationCanceledException"/> from a genuine
/// host shutdown), so a bad boot-time DB day never blocks the host from starting; the next boot
/// retries (the SafeLoopSeeder F27.6 posture, mirrored here).
/// </para>
/// </summary>
public sealed class AdsLibrarySeeder(
    ILibraryRepository libraryRepository,
    IAdminLibraryWrite libraryWriter,
    IOptionsMonitor<AdsOptions> adsOptions,
    ILogger<AdsLibrarySeeder> logger)
{
    public async Task<AdsLibrarySeedOutcome> SeedAsync(CancellationToken ct)
    {
        var name = adsOptions.CurrentValue.LibraryName;

        try
        {
            var existing = await FindAsync(name, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                logger.LogInformation(
                    "Boot seed: ads library \"{LibraryName}\" (id={LibraryId}) already present — reusing",
                    name, existing.Id);
                return AdsLibrarySeedOutcome.AlreadySeeded;
            }

            var created = await libraryWriter.CreateAsync(name, ct).ConfigureAwait(false);
            if (created is LibraryWriteResult.Created ok)
            {
                logger.LogInformation("Boot seed: ads library \"{LibraryName}\" created (id={LibraryId})", name, ok.Id);
                return AdsLibrarySeedOutcome.Seeded;
            }

            if (created is LibraryWriteResult.NameConflict)
            {
                // An operator's own POST /api/libraries, or a concurrent boot on another replica,
                // raced this create — re-look-up and reuse rather than fail (mirrors
                // SafeLoopSeeder.EnsureSafeLibraryAsync's identical race handling).
                var afterRace = await FindAsync(name, ct).ConfigureAwait(false);
                if (afterRace is not null)
                {
                    logger.LogInformation(
                        "Boot seed: ads library \"{LibraryName}\" (id={LibraryId}) created concurrently — reusing",
                        name, afterRace.Id);
                    return AdsLibrarySeedOutcome.AlreadySeeded;
                }
            }

            logger.LogWarning(
                "Boot seed: could not create or find ads library \"{LibraryName}\" (create result: {Result}) " +
                "— host starting normally, will retry on next boot",
                name, created);
            return AdsLibrarySeedOutcome.Failed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Boot seed: ads library seed failed — host starting normally, will retry on next boot");
            return AdsLibrarySeedOutcome.Failed;
        }
    }

    async Task<LibraryAdminInfo?> FindAsync(string name, CancellationToken ct)
    {
        var libraries = await libraryRepository.GetAllWithMediaCountAsync(ct).ConfigureAwait(false);
        foreach (var library in libraries)
        {
            if (string.Equals(library.Name, name, StringComparison.Ordinal))
                return library;
        }

        return null;
    }
}
