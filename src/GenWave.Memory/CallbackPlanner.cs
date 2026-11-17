using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Memory;

/// <summary>
/// Default <see cref="ICallbackPlanner"/> (SPEC F218.41, F218.44, F218.45). Order: segment kind, setting, the two
/// offer-age gates (database clock), then the 0.25 draw, then eligible memories, then the offer write. The draw is
/// taken only after the cheap gates pass, so a seeded <see cref="Random"/> sees exactly one value per fully gated
/// break; no offer is written unless everything passes.
/// </summary>
public sealed class CallbackPlanner(ICallbackStore store, Random? random = null) : ICallbackPlanner
{
    public const int MaxCallbacksPerHour = 6;
    public const double DrawChance = 0.25;
    public static readonly TimeSpan UsedWindow = TimeSpan.FromMinutes(45);
    public static readonly TimeSpan AnyOfferWindow = TimeSpan.FromMinutes(10);

    readonly Random random = random ?? Random.Shared;

    public async Task<CallbackAsk?> PlanAsync(CallbackRequest request, CancellationToken ct)
    {
        if (request.Kind is not (SegmentKind.LeadIn or SegmentKind.BackAnnounce) || request.CallbacksPerHour <= 0)
            return null;

        var perHour = Math.Min(request.CallbacksPerHour, MaxCallbacksPerHour);
        var history = await store.LastOfferAsync(request.PersonaId, ct);
        if (history.SinceLastUsed is { } sinceUsed && sinceUsed <= UsedWindow / perHour)
            return null;
        if (history.SinceLastAsk is { } sinceAsk && sinceAsk <= AnyOfferWindow)
            return null;
        if (random.NextDouble() >= DrawChance)
            return null;

        var exclusions = new CallbackExclusions(request.ExclusionTexts);
        var candidates = await store.EligibleAsync(request.PersonaId, ct);
        var memory = candidates.FirstOrDefault(m => CallbackWordRule.HasDistinctiveWord(m.Content, exclusions));
        if (memory is null)
            return null;

        var offerId = await store.OfferAsync(memory.Id, request.PersonaId, ct);
        return new CallbackAsk(offerId, memory, request.ExclusionTexts);
    }

    public Task SettleAsync(CallbackAsk ask, long? utteranceId, string? line, bool fallback, CancellationToken ct)
    {
        if (fallback || line is null || utteranceId is null)
            return store.SettleAsync(ask.OfferId, utteranceId, false, [], ct);

        var match = CallbackWordRule.Used(ask.Memory.Content, line, new CallbackExclusions(ask.ExclusionTexts));
        return store.SettleAsync(ask.OfferId, utteranceId, match.Used, match.Words, ct);
    }
}
