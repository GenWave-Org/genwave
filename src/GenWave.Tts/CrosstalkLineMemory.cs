namespace GenWave.Tts;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using Microsoft.Extensions.Logging;

/// <summary>
/// The memory half of <see cref="CrosstalkScriptWriter"/> (SPEC F218.14, STORY-535): reads each speaker's
/// own recent utterances, finds the lines that repeat THEIR history (never the other speaker's), and
/// stores the lines that air. A side without a persona id, or a writer without a check/store, does
/// nothing here — the exchange then behaves exactly as before the memory layer existed.
/// </summary>
internal sealed class CrosstalkLineMemory(
    IRepetitionCheck? repetitionCheck, IUtteranceStore? utteranceStore, IStationEventSink events,
    ILogger logger)
{
    /// <summary>How many of the speaker's newest non-template utterances a line is compared against (SPEC F218.11).</summary>
    const int Window = 10;

    /// <summary>The <c>segment_kind</c> every stored crosstalk line carries.</summary>
    public const string SegmentKind = "CrosstalkLine";

    /// <summary>One speaker's own non-template recent texts, newest first.</summary>
    public sealed record SpeakerHistory(CrosstalkSpeaker Speaker, long PersonaId, string PersonaName, IReadOnlyList<string> Texts);

    /// <summary>A line that repeats its own speaker's history: its index in the script and the phrases that hit.</summary>
    public sealed record LineHit(int LineIndex, long PersonaId, IReadOnlyList<string> Phrases);

    public async Task<IReadOnlyList<SpeakerHistory>> ReadHistoriesAsync(CrosstalkExchangeRequest request, CancellationToken ct)
    {
        if (repetitionCheck is null || utteranceStore is null)
            return [];

        var histories = new List<SpeakerHistory>(2);
        if (request.HostPersonaId is { } hostId)
            histories.Add(await ReadAsync(CrosstalkSpeaker.Host, hostId, request.HostCard.Name, utteranceStore, ct));
        if (request.NeighborPersonaId is { } neighborId)
            histories.Add(await ReadAsync(CrosstalkSpeaker.Neighbor, neighborId, request.NeighborCard.Name, utteranceStore, ct));
        return histories;
    }

    /// <summary>Lines of <paramref name="script"/> that repeat their own speaker's history; empty when there is nothing to check.</summary>
    public IReadOnlyList<LineHit> FindRepeats(
        CrosstalkAiredScript script, string stationName, IReadOnlyList<SpeakerHistory> histories)
    {
        if (repetitionCheck is null)
            return [];

        var hits = new List<LineHit>();
        for (var i = 0; i < script.Lines.Count; i++)
        {
            var line = script.Lines[i];
            var history = histories.FirstOrDefault(h => h.Speaker == line.Speaker);
            if (history is null)
                continue;

            // Crosstalk selects no capped quirks (T688), so there are no spent rules; the exclusions are
            // the speaker's own name and the station's — saying either is not a repeat.
            var result = repetitionCheck.Check(
                line.Text, new RepetitionContext(history.Texts, [], history.PersonaName, stationName));
            if (result.IsHit)
                hits.Add(new LineHit(i, history.PersonaId, result.Hits.Select(h => h.Phrase).ToList()));
        }
        return hits;
    }

    /// <summary>One <c>repeat-caught</c> row per speaker that had a hit, naming that speaker's phrases (SPEC F218.13).</summary>
    public void PublishCaught(IReadOnlyList<LineHit> hits)
    {
        foreach (var speaker in hits.GroupBy(h => h.PersonaId))
            events.Publish(new RepeatCaught(speaker.Key, string.Join(", ", speaker.SelectMany(h => h.Phrases))));
    }

    /// <summary>Records every aired line under its speaker (SPEC F218.14, STORY-535 AC2). A fault costs the record, never the exchange.</summary>
    public async Task StoreAsync(CrosstalkExchangeRequest request, CrosstalkAiredScript script, CancellationToken ct)
    {
        if (utteranceStore is null)
            return;

        foreach (var line in script.Lines)
        {
            var personaId = line.Speaker == CrosstalkSpeaker.Host ? request.HostPersonaId : request.NeighborPersonaId;
            if (personaId is not { } id)
                continue;

            // Crosstalk is stocked ahead and spends no capped quirks (T688), so its shift key only dates the line.
            var shiftKey = ShiftKey.Unscheduled(id, DateOnly.FromDateTime(request.StationLocalNow.DateTime));
            try
            {
                await utteranceStore.RecordAsync(
                    new NewUtterance(id, request.ShowId, UtteranceKind.Crosstalk, SegmentKind, shiftKey, line.Text, Fallback: false),
                    ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Storing a crosstalk line for persona {PersonaId} failed; the exchange still airs", id);
            }
        }
    }

    async Task<SpeakerHistory> ReadAsync(
        CrosstalkSpeaker speaker, long personaId, string personaName, IUtteranceStore store, CancellationToken ct)
    {
        try
        {
            var recent = await store.RecentAsync(personaId, Window, ct);
            return new SpeakerHistory(speaker, personaId, personaName, recent.Where(e => !e.Fallback).Select(e => e.Text).ToList());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Recent-lines read failed for crosstalk persona {PersonaId}; its lines go unchecked", personaId);
            return new SpeakerHistory(speaker, personaId, personaName, []);
        }
    }
}
