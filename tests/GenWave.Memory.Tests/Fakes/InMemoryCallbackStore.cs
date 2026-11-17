using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Memory.Tests.Fakes;

/// <summary>
/// In-memory stand-in for <see cref="ICallbackStore"/> on an injectable clock, honouring the F218.43-F218.46 rules
/// (rests, eligibility, ordering, 28-day eviction, settle counters). The SQL itself is pinned by
/// GenWave.MediaLibrary.Tests Story550.
/// </summary>
public sealed class InMemoryCallbackStore(TimeProvider time) : ICallbackStore
{
    static readonly TimeSpan LoreRest = TimeSpan.FromHours(6);
    static readonly TimeSpan OtherRest = TimeSpan.FromHours(48);
    static readonly TimeSpan AskRest = TimeSpan.FromHours(2);
    static readonly TimeSpan Retention = TimeSpan.FromDays(28);

    public sealed class Memory
    {
        public long Id { get; init; }
        public long PersonaId { get; init; }
        public required string Kind { get; init; }
        public required string Content { get; init; }
        public bool Forgotten { get; init; }
        public bool UnapprovedDraft { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public int AiredCount { get; set; }
        public DateTimeOffset? LastAiredAt { get; set; }
    }

    public sealed class Offer
    {
        public long Id { get; init; }
        public long MemoryId { get; init; }
        public long PersonaId { get; init; }
        public DateTimeOffset OfferedAt { get; set; }
        public long? UtteranceId { get; set; }
        public bool Used { get; set; }
        public IReadOnlyList<string> MatchedWords { get; set; } = [];
    }

    readonly List<Memory> memories = [];
    readonly List<Offer> offers = [];
    long lastMemoryId;
    long lastOfferId;

    public IReadOnlyList<Memory> Memories => memories;

    public IReadOnlyList<Offer> Offers => offers;

    /// <summary>Plants a memory; <paramref name="age"/> is how long ago it was created.</summary>
    public Memory Seed(long personaId, string kind, string content, TimeSpan? age = null, bool forgotten = false, bool unapprovedDraft = false)
    {
        var memory = new Memory
        {
            Id = ++lastMemoryId, PersonaId = personaId, Kind = kind, Content = content, Forgotten = forgotten,
            UnapprovedDraft = unapprovedDraft, CreatedAt = time.GetUtcNow() - (age ?? TimeSpan.FromDays(1)),
        };
        memories.Add(memory);
        return memory;
    }

    /// <summary>Plants an ask made <paramref name="ago"/> ago, used or not (a used one also sets the memory's last aired).</summary>
    public Offer SeedOffer(Memory memory, TimeSpan ago, bool used = false)
    {
        var offer = new Offer { Id = ++lastOfferId, MemoryId = memory.Id, PersonaId = memory.PersonaId, OfferedAt = time.GetUtcNow() - ago, Used = used };
        offers.Add(offer);
        if (used)
        {
            memory.AiredCount++;
            memory.LastAiredAt = offer.OfferedAt;
        }

        return offer;
    }

    public Task<IReadOnlyList<RecalledMemory>> EligibleAsync(long personaId, int take, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        DateTimeOffset? LastAsk(Memory m) => offers.Where(o => o.MemoryId == m.Id).Select(o => (DateTimeOffset?)o.OfferedAt).Max();

        IReadOnlyList<RecalledMemory> result = memories
            .Where(m => m.PersonaId == personaId && m.Kind is "lore" or "episode" or "life")
            .Where(m => !m.Forgotten && !m.UnapprovedDraft)
            .Where(m => m.LastAiredAt is not { } aired || aired <= now - (m.Kind == "lore" ? LoreRest : OtherRest))
            .Where(m => LastAsk(m) is not { } ask || ask <= now - AskRest)
            .OrderBy(m => LastAsk(m) is null ? 0 : 1).ThenBy(LastAsk).ThenByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(take)
            .Select(m => new RecalledMemory(m.Id, m.Kind, m.Content, null))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<CallbackOfferHistory> LastOfferAsync(long personaId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var mine = offers.Where(o => o.PersonaId == personaId).ToList();
        TimeSpan? Since(IEnumerable<Offer> set) => set.Select(o => (DateTimeOffset?)o.OfferedAt).Max() is { } last ? now - last : null;
        return Task.FromResult(new CallbackOfferHistory(Since(mine.Where(o => o.Used)), Since(mine)));
    }

    public Task<long> OfferAsync(long memoryId, long personaId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        offers.RemoveAll(o => o.OfferedAt < now - Retention);
        var offer = new Offer { Id = ++lastOfferId, MemoryId = memoryId, PersonaId = personaId, OfferedAt = now };
        offers.Add(offer);
        return Task.FromResult(offer.Id);
    }

    public Task SettleAsync(long offerId, long? utteranceId, bool used, IReadOnlyList<string> matchedWords, CancellationToken ct)
    {
        var offer = offers.SingleOrDefault(o => o.Id == offerId);
        if (offer is null || offer.Used)
            return Task.CompletedTask;

        offer.UtteranceId = utteranceId;
        offer.Used = used;
        offer.MatchedWords = matchedWords.ToList();
        if (used && memories.SingleOrDefault(m => m.Id == offer.MemoryId) is { } memory)
        {
            memory.AiredCount++;
            memory.LastAiredAt = time.GetUtcNow();
        }

        return Task.CompletedTask;
    }
}
