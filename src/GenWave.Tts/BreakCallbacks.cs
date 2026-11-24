using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;

namespace GenWave.Tts;

/// <summary>
/// The writer's door to the callback planner (SPEC F218.40-F218.45): builds the planner's request for one on-air break
/// and keeps both calls fail-open, so a planner, store or roster fault costs the break its callback and never the
/// line (F12.4). The memory-on and on-air checks belong to the caller; this asks only the segment-kind question.
/// </summary>
public sealed class BreakCallbacks(
    ICallbackPlanner planner, ICallbackRate rate, IPersonaStore personas, ILogger<BreakCallbacks> logger) : IBreakCallbacks
{
    /// <summary>
    /// The ask for this break, or null when it is not a callback break or anything faulted. The offer row is written
    /// before this returns (F218.44), so the caller renders afterwards.
    /// </summary>
    public async Task<CallbackAsk?> AskAsync(SegmentRequest request, Persona? persona, PersonaCard? card, CancellationToken ct)
    {
        if (request.PersonaId is not { } personaId)
            return null;

        try
        {
            return await PlanStrictAsync(CallbackBreak.Of(request, personaId), persona, card, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Callback planning failed for {Kind}; this break airs without a callback", request.Kind);
            return null;
        }
    }

    /// <summary>
    /// The ask for this break with no fail-open guard: a fault propagates, for a caller that reports it itself (the rule book,
    /// F220.9). Null when the kind carries no callback or the setting is 0. <see cref="AskAsync"/> is this, kept fail-open.
    /// </summary>
    public async Task<CallbackAsk?> PlanStrictAsync(CallbackBreak callbackBreak, Persona? persona, PersonaCard? card, CancellationToken ct)
    {
        if (!CallbackKinds.Carries(callbackBreak.Kind))
            return null;

        var perHour = rate.CallbacksPerHour;
        if (perHour <= 0)
            return null;

        var exclusions = await ExclusionTextsAsync(callbackBreak, persona, card, ct);
        return await planner.PlanAsync(new CallbackRequest(callbackBreak.PersonaId, callbackBreak.Kind, perHour, exclusions), ct);
    }

    /// <summary>
    /// Settles the offer with the line (F218.44) with no fail-open guard: a fault propagates, for a caller that reports it
    /// itself (the rule book, F220.9). A fallback line, or one with no utterance id, settles unused.
    /// </summary>
    public Task SettleStrictAsync(CallbackAsk ask, long? utteranceId, string? line, bool fallback, CancellationToken ct) =>
        planner.SettleAsync(ask, utteranceId, line, fallback, ct);

    /// <summary>
    /// Settles the offer with the recorded line (F218.44). A fallback line, or one with no utterance id, settles unused.
    /// A fault is logged and leaves the offer unused.
    /// </summary>
    public async Task SettleAsync(CallbackAsk ask, long? utteranceId, string? line, bool fallback, CancellationToken ct)
    {
        try
        {
            await SettleStrictAsync(ask, utteranceId, line, fallback, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Callback offer {OfferId} could not be settled; it stays unused", ask.OfferId);
        }
    }

    /// <summary>
    /// F218.45: every DJ's name, the station name, this break's track, artist and album, and the speaking DJ's card
    /// text. Never lore: that is the memory itself.
    /// </summary>
    async Task<IReadOnlyList<string?>> ExclusionTextsAsync(
        CallbackBreak callbackBreak, Persona? persona, PersonaCard? card, CancellationToken ct)
    {
        var texts = new List<string?> { callbackBreak.StationName, callbackBreak.Title, callbackBreak.Artist, callbackBreak.Album };
        texts.AddRange((await personas.GetAllAsync(ct)).Select(p => p.Name));
        if (persona is not null)
            texts.AddRange([persona.Name, persona.Style, persona.Backstory]);
        if (card is not null)
        {
            texts.AddRange([card.Name, card.Tagline, card.Soul]);
            texts.AddRange(card.Quirks);
        }

        return texts;
    }
}
