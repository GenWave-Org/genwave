using System.Data.Common;
using Microsoft.Extensions.Options;
using GenWave.Ads;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Options;
using GenWave.Tts;

namespace GenWave.Host.Seeding;

/// <summary>
/// The Status count of items still in the old voice (SPEC F219.23): stale ready ad spots plus stale airable ledger items.
/// It asks the questions the two sweeps ask, through the same predicates (<see cref="IStaleAdSpotCounter"/> over the ad
/// pass's own WHERE; <see cref="SafeContentMarker.IsStale"/> and <see cref="AdminMediaAirability.CanStillAir"/> for the
/// seeder), with the current engine and voices read live, so the number reaches zero when every airable item has been
/// re-rendered (an item a sweep gave up on or held back stays counted: it really is still in the old voice).
/// Read-only: unlike the sweep it never drops a ledger entry.
///
/// Fails closed: when either half cannot be read, or the station has no voice for the engine (so there is no current
/// marker to compare against), the answer is null, not a partial count that might read as "all done". Only database and
/// argument faults degrade, as in the sibling Status readers; a plain bug and caller cancellation surface.
/// <see cref="DbException"/> is caught because it is Npgsql's base type; Npgsql itself stays in the repository layer.
/// A damaged ledger is already read as empty by the ledger itself.
/// </summary>
public sealed class StaleVoiceItemsReader(
    IStaleAdSpotCounter adCounter,
    ISafeContentLedger ledger,
    ISeedRowFinder seedRowFinder,
    IAdminMediaLookup mediaLookup,
    IVoiceResolver voiceResolver,
    IActiveVoiceEngine activeEngine,
    IConfiguration configuration,
    IOptionsMonitor<StationOptions> stationMonitor,
    ILogger<StaleVoiceItemsReader> logger) : IStaleVoiceItemsReader
{
    public async Task<int?> CountAsync(CancellationToken ct)
    {
        try
        {
            if (voiceResolver.ResolveSetting(stationMonitor.CurrentValue.Voice) is not { } voice)
                return null;

            var ads = await adCounter.CountStaleReadyAsync(
                AdRenderMarker.For(AdLiveSettingsReader.Read(configuration, activeEngine)), ct);
            return ads + await CountStaleSeederItemsAsync(SafeContentMarker.For(activeEngine.Name, voice), ct);
        }
        catch (Exception ex) when (ex is DbException or ArgumentException)
        {
            logger.LogWarning(ex, "Could not count the items still in the old voice; reporting it as unknown");
            return null;
        }
    }

    async Task<int> CountStaleSeederItemsAsync(string marker, CancellationToken ct)
    {
        var count = 0;
        foreach (var record in await SafeContentLedgerReader.ListWithAdoptedSeedAsync(ledger, seedRowFinder, ct))
        {
            if (SafeContentMarker.IsStale(record, marker)
                && AdminMediaAirability.CanStillAir((await mediaLookup.GetByIdWithLibraryAsync(record.MediaId, ct))?.Row))
                count++;
        }

        return count;
    }
}
