namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>An <see cref="ICallbackStore"/> double holding one eligible memory and keeping every offer and settle it is handed.</summary>
public sealed class RecordingCallbackStore(string memoryText) : ICallbackStore
{
    public sealed record Offer(long Id, long MemoryId, long PersonaId)
    {
        public long? UtteranceId { get; set; }
        public bool Used { get; set; }
        public IReadOnlyList<string> MatchedWords { get; set; } = [];
        public bool Settled { get; set; }
    }

    readonly List<Offer> offers = [];

    public IReadOnlyList<Offer> Offers => offers;

    /// <summary>When true every write throws, as a store outage would.</summary>
    public bool Throws { get; init; }

    public int EligibleReads { get; private set; }

    public Task<IReadOnlyList<RecalledMemory>> EligibleAsync(long personaId, CancellationToken ct)
    {
        EligibleReads++;
        return Task.FromResult<IReadOnlyList<RecalledMemory>>([new RecalledMemory(11, MemoryKinds.Life, memoryText, null)]);
    }

    public Task<CallbackOfferHistory> LastOfferAsync(long personaId, CancellationToken ct) =>
        Task.FromResult(new CallbackOfferHistory(null, null));

    public Task<long> OfferAsync(long memoryId, long personaId, CancellationToken ct)
    {
        if (Throws)
            throw new InvalidOperationException("callback store down");

        var offer = new Offer(offers.Count + 1, memoryId, personaId);
        offers.Add(offer);
        return Task.FromResult(offer.Id);
    }

    public Task SettleAsync(long offerId, long? utteranceId, bool used, IReadOnlyList<string> matchedWords, CancellationToken ct)
    {
        if (Throws)
            throw new InvalidOperationException("callback store down");

        var offer = offers.Single(o => o.Id == offerId);
        offer.UtteranceId = utteranceId;
        offer.Used = used;
        offer.MatchedWords = matchedWords;
        offer.Settled = true;
        return Task.CompletedTask;
    }
}
