using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>An <see cref="ICallbackStore"/> holding one eligible memory (or none) and keeping every offer and settle it is handed.</summary>
internal sealed class CallbackStoreOfOneMemory(string? memoryText) : ICallbackStore
{
    public sealed record Offer(long Id, long MemoryId, long PersonaId)
    {
        public bool Settled { get; set; }
        public long? UtteranceId { get; set; }
        public bool Used { get; set; }
        public IReadOnlyList<string> MatchedWords { get; set; } = [];
    }

    private readonly List<Offer> offers = [];

    public IReadOnlyList<Offer> Offers => offers;

    public bool ThrowOnOffer { get; init; }

    public bool ThrowOnSettle { get; init; }

    public Task<IReadOnlyList<RecalledMemory>> EligibleAsync(long personaId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RecalledMemory>>(memoryText is null ? [] : [new RecalledMemory(11, MemoryKinds.Life, memoryText, null)]);

    public Task<CallbackOfferHistory> LastOfferAsync(long personaId, CancellationToken ct) =>
        Task.FromResult(new CallbackOfferHistory(null, null));

    public Task<long> OfferAsync(long memoryId, long personaId, CancellationToken ct)
    {
        if (ThrowOnOffer)
            throw new InvalidOperationException("callback store down");

        var offer = new Offer(offers.Count + 1, memoryId, personaId);
        offers.Add(offer);
        return Task.FromResult(offer.Id);
    }

    public Task SettleAsync(long offerId, long? utteranceId, bool used, IReadOnlyList<string> matchedWords, CancellationToken ct)
    {
        if (ThrowOnSettle)
            throw new InvalidOperationException("callback store down");

        var offer = offers.Single(o => o.Id == offerId);
        offer.Settled = true;
        offer.UtteranceId = utteranceId;
        offer.Used = used;
        offer.MatchedWords = matchedWords;
        return Task.CompletedTask;
    }
}
