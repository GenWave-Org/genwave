using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;
using GenWave.Host.Options;
using GenWave.Tts;

namespace GenWave.Host.Seeding;

/// <summary>
/// Brings the boot seed's station-voice content onto the voice the station has now (SPEC F219.22, F219.24). Each
/// idle tick it re-renders at most ONE ledger item whose marker differs from the current engine and voice. Idle is
/// the same signal the ad stale pass gates on: <see cref="IOnAirRenderSignal.InFlight"/> false.
///
/// The old file keeps airing until the new one is in: the replacement is authored first (its own row, airable on
/// insert), then the old row is switched off, never deleted. A failed or slow render leaves the old row exactly as
/// it was. A failure holds that marker back for <see cref="RetryAfter"/> (no spinning on a broken engine) but a
/// changed marker, such as the operator fixing the voice, is tried straight away — unlike the ad pass, nothing is
/// skipped for the life of the process. Only ledger items are touched, so uploaded imaging is never re-rendered.
///
/// A render yields to an on-air break window: a watchdog beside the render cancels it the moment
/// <see cref="IOnAirRenderSignal.InFlight"/> reads true (mirrors <c>AdSpotWorker.WatchOnAirRenderAsync</c>). A yield
/// holds nothing back — the next tick retries — and leaves the old row and the ledger as they were.
///
/// A ledger item whose row is gone, switched off or never_play is dropped from the ledger: Dean turned it off, there
/// is nothing airing to replace, and keeping it would pin it as "still in the old voice" forever and, being first in
/// line, stall the items behind it. Dropping it is also how it logs once, not every tick.
/// </summary>
public sealed class SafeContentResweeper(
    ISafeContentLedger ledger,
    ISeedRowFinder seedRowFinder,
    ISafeSegmentAuthor author,
    IAdminMediaLookup mediaLookup,
    IAuthoredCatalogWriter catalogWriter,
    IVoiceResolver voiceResolver,
    IActiveVoiceEngine activeEngine,
    IRenderBudgetProvider renderBudgetProvider,
    IOptionsMonitor<StationOptions> stationMonitor,
    IOnAirRenderSignal onAirRenderSignal,
    TimeProvider timeProvider,
    ILogger<SafeContentResweeper> logger) : BackgroundService
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(15);

    /// <summary>How often a running render checks whether a break window has opened.</summary>
    public static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(1);

    string? failedMarker;
    DateTimeOffset failedAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TickInterval, timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await TickOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected: host shutdown
        }
    }

    /// <summary>One tick, callable without the timer. Only a cancelled <paramref name="ct"/> escapes; any other fault is logged and the next tick tries again.</summary>
    public async Task TickOnceAsync(CancellationToken ct)
    {
        if (onAirRenderSignal.InFlight)
            return;

        try
        {
            await SweepOneAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Safe content re-render sweep failed; trying again next tick");
        }
    }

    async Task SweepOneAsync(CancellationToken ct)
    {
        var station = stationMonitor.CurrentValue;
        if (voiceResolver.ResolveSetting(station.Voice) is not { } voice)
            return; // No voice for this engine: nothing can be re-rendered, and the old files keep airing.

        var records = await SafeContentLedgerReader.ListWithAdoptedSeedAsync(ledger, seedRowFinder, ct);

        await FinishPendingRetiresAsync(records, ct);

        var marker = SafeContentMarker.For(activeEngine.Name, voice);
        if (IsHeldBack(marker))
            return;

        if (await FindStaleAirableAsync(records, marker, ct) is not { } stale)
            return;

        await ReRenderAsync(records, stale.Record, stale.LibraryId, station, voice, marker, ct);
    }

    /// <summary>
    /// The first stale item whose row can still air, with its library. Stale items that cannot (gone, switched off,
    /// never_play) are dropped from <paramref name="records"/> and the ledger on the way, so they never block the
    /// items behind them. An item still waiting on a retire is left alone either way.
    /// </summary>
    async Task<(SafeContentRecord Record, long LibraryId)?> FindStaleAirableAsync(
        List<SafeContentRecord> records, string marker, CancellationToken ct)
    {
        (SafeContentRecord, long)? found = null;
        var dropped = false;

        foreach (var record in records.Where(r => SafeContentMarker.IsStale(r, marker)).ToList())
        {
            if (await mediaLookup.GetByIdWithLibraryAsync(record.MediaId, ct) is { } row
                && AdminMediaAirability.CanStillAir(row.Row))
            {
                found = (record, row.LibraryId);
                break;
            }

            records.Remove(record);
            dropped = true;
            logger.LogInformation(
                "Safe content media {MediaId} is gone or switched off; dropping it from the re-render ledger", record.MediaId);
        }

        if (dropped)
            await ledger.SaveAsync(records, ct);
        return found;
    }

    bool IsHeldBack(string marker) =>
        marker == failedMarker && timeProvider.GetUtcNow() < failedAt + RetryAfter;

    /// <summary>
    /// Finishes a swap an earlier tick began: the old row is switched off only while its replacement is still airable.
    /// If the replacement is gone or off (a ledger save that committed and then threw, so the new row was switched
    /// off), the old row stays the live take and the item goes back to it, unmarked, to be replaced again.
    /// </summary>
    async Task FinishPendingRetiresAsync(List<SafeContentRecord> records, CancellationToken ct)
    {
        var changed = false;
        for (var i = 0; i < records.Count; i++)
        {
            if (records[i].PendingRetireMediaId is not { } oldId)
                continue;

            if (await mediaLookup.GetByIdWithLibraryAsync(records[i].MediaId, ct) is { Row.Eligible: true })
            {
                if (!await TryRetireAsync(oldId, ct))
                    continue;
                records[i] = records[i] with { PendingRetireMediaId = null };
            }
            else
            {
                logger.LogWarning(
                    "Safe content media {NewMediaId} is not airable; keeping {OldMediaId} as the live take", records[i].MediaId, oldId);
                records[i] = new SafeContentRecord(oldId, Marker: string.Empty);
            }

            changed = true;
        }

        if (changed)
            await ledger.SaveAsync(records, ct);
    }

    async Task ReRenderAsync(
        List<SafeContentRecord> records, SafeContentRecord stale, long libraryId, StationOptions station, string voice,
        string marker, CancellationToken ct)
    {
        var result = await RenderYieldingToBreaksAsync(
            SeedSegmentRequests.Build(station, voice, libraryId), stale.MediaId, marker, ct);
        if (result is null)
            return;

        var index = records.IndexOf(stale);
        records[index] = new SafeContentRecord(result.MediaId, marker, PendingRetireMediaId: stale.MediaId);
        if (!await TrySaveOrSwitchOffNewRowAsync(records, result.MediaId, marker, ct))
            return;

        failedMarker = null;
        logger.LogInformation("Safe content media {OldMediaId} re-rendered as {NewMediaId}", stale.MediaId, result.MediaId);

        if (await TryRetireAsync(stale.MediaId, ct))
        {
            records[index] = records[index] with { PendingRetireMediaId = null };
            await ledger.SaveAsync(records, ct);
        }
    }

    /// <summary>
    /// The author inserts the new row airable, so a ledger that cannot record it would leave it airing untracked and
    /// the next tick inserting another. Fail closed instead: switch the new row off (best effort, not tied to the
    /// caller's token), hold the marker back, and leave the old row airing.
    /// </summary>
    async Task<bool> TrySaveOrSwitchOffNewRowAsync(
        IReadOnlyList<SafeContentRecord> records, long newMediaId, string marker, CancellationToken ct)
    {
        try
        {
            await ledger.SaveAsync(records, ct);
            return true;
        }
        catch (Exception ex)
        {
            await TryRetireAsync(newMediaId, CancellationToken.None);
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;

            HoldBack(marker);
            logger.LogWarning(ex, "Safe content ledger save failed; switched off new media {MediaId}, old file keeps airing", newMediaId);
            return false;
        }
    }

    /// <summary>
    /// Renders one replacement. Null means nothing was made: the render failed or ran past its budget (held back),
    /// or a break window opened and it yielded (not held back, so the next tick retries).
    /// </summary>
    async Task<SafeSegmentAuthorResult?> RenderYieldingToBreaksAsync(
        SafeSegmentRequest request, long staleMediaId, string marker, CancellationToken ct)
    {
        var renderBudget = renderBudgetProvider.Current;
        using var budget = new CancellationTokenSource(renderBudget, timeProvider);
        using var render = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
        using var watchdogStop = CancellationTokenSource.CreateLinkedTokenSource(ct);

        logger.LogInformation(
            "Safe content media {MediaId} re-render starting budgetSeconds={BudgetSeconds}", staleMediaId, renderBudget.TotalSeconds);

        var breakWindowOpened = false;
        var watchdog = WatchOnAirRenderAsync(render, () => breakWindowOpened = true, watchdogStop.Token);

        try
        {
            var result = await author.AuthorAsync(request, render.Token);
            if (result.Succeeded)
                return result;

            HoldBack(marker);
            logger.LogWarning(
                "Safe content media {MediaId} re-render failed reason={Reason} detail={Detail}; old file keeps airing",
                staleMediaId, result.FailureReason, LogSanitize.Strip(result.FailureDetail));
            return null;
        }
        catch (OperationCanceledException) when (render.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            if (breakWindowOpened)
                logger.LogInformation("Safe content media {MediaId} re-render yielded to an on-air break; retrying next tick", staleMediaId);
            else
            {
                HoldBack(marker);
                logger.LogWarning("Safe content media {MediaId} re-render exceeded budgetSeconds={BudgetSeconds}; old file keeps airing", staleMediaId, renderBudget.TotalSeconds);
            }

            return null;
        }
        finally
        {
            await watchdogStop.CancelAsync();
            await watchdog;
        }
    }

    /// <summary>Cancels <paramref name="render"/> the instant a break window is in flight, flagging why first so the caller can tell a yield from the budget timeout.</summary>
    async Task WatchOnAirRenderAsync(CancellationTokenSource render, Action onBreakWindowOpened, CancellationToken stop)
    {
        try
        {
            using var timer = new PeriodicTimer(WatchdogInterval, timeProvider);
            while (await timer.WaitForNextTickAsync(stop))
            {
                if (render.IsCancellationRequested)
                    return;

                if (onAirRenderSignal.InFlight)
                {
                    onBreakWindowOpened();
                    await render.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // the render finished first, or the host is stopping — nothing left to interrupt
        }
    }

    void HoldBack(string marker)
    {
        failedMarker = marker;
        failedAt = timeProvider.GetUtcNow();
    }

    /// <summary>Takes a replaced row off air. It is switched off, not deleted; the authored-file sweep owns removal.</summary>
    async Task<bool> TryRetireAsync(long mediaId, CancellationToken ct)
    {
        try
        {
            await catalogWriter.SetEligibleAsync(mediaId, false, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not switch off replaced safe content media {MediaId}; will retry", mediaId);
            return false;
        }
    }
}
