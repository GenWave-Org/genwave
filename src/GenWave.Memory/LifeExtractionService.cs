using System.Threading.Channels;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;
using Microsoft.Extensions.Logging;

namespace GenWave.Memory;

/// <summary>
/// SPEC F218.17-F218.18 (STORY-537) — turns the personal claims in an accepted copy into the <c>life</c> rows a
/// persona remembers. The copy writer only <see cref="Enqueue"/>s (a bounded queue write, never throws);
/// <see cref="RunAsync"/> drains it one copy at a time, so the model never sees two extraction calls at once and
/// nothing here is on the air path. A reply that is not a JSON array of claims is logged at Debug and stores nothing.
/// <para>
/// A claim stores as the speaker's own <c>life</c> row. When its <c>about</c> names another persona on the roster
/// it becomes an <see cref="MemoryEventKinds.Outing"/> event and one row each side (<see cref="OutingPhrasing"/>).
/// An <c>about</c> that names nobody on the roster is a claim about the speaker alone.
/// </para>
/// <para>
/// The per-window cap (<see cref="LifeMemoryRules.MaxNewPerWindow"/> per <see cref="LifeMemoryRules.NewWindow"/>)
/// holds for both DJs of an outing: rows added earlier in the same pass count too. When the other DJ has no room
/// left, only the speaker's own row is stored (with <see cref="ILifeStore.AddAsync"/>, no outing event), so a DJ
/// never gains rows past the cap by being named.
/// </para>
/// </summary>
public sealed class LifeExtractionService(
    ILifeClaimLlm llm,
    ILifeStore store,
    IPersonaStore personas,
    ILlmBatchGate gate,
    ILifeExtractionSwitch enabled,
    TimeProvider time,
    ILogger<LifeExtractionService> logger) : ILifeExtractor
{
    /// <summary>Most copies waiting for the model; the newest is dropped past this.</summary>
    public const int QueueCapacity = 16;

    /// <summary>A drop is logged the first time and then every this many drops, so a stalled model cannot flood the log.</summary>
    const int DropLogEvery = 10;

    readonly Channel<AcceptedCopy> queue = Channel.CreateBounded<AcceptedCopy>(
        new BoundedChannelOptions(QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    long droppedCopies;

    /// <summary>Copies lost because the queue was full (each is logged, the first and then every tenth).</summary>
    public long DroppedCopies => Interlocked.Read(ref droppedCopies);

    public void Enqueue(AcceptedCopy copy)
    {
        if (!enabled.IsEnabled)
            return;

        if (queue.Writer.TryWrite(copy))
            return;

        var dropped = Interlocked.Increment(ref droppedCopies);
        if (dropped == 1 || dropped % DropLogEvery == 0)
            logger.LogInformation(
                "Life extraction queue is full ({Capacity}); dropped a copy from {Persona} ({Dropped} dropped so far)",
                QueueCapacity, LogSanitize.Strip(copy.PersonaName), dropped);
    }

    /// <summary>
    /// Drains queued copies until <paramref name="ct"/> is cancelled or <see cref="Complete"/> is called and the
    /// queue empties. A fault on one copy goes to <paramref name="onFault"/> and that copy is lost.
    /// </summary>
    public async Task RunAsync(Action<Exception> onFault, CancellationToken ct)
    {
        await foreach (var copy in queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                await ExtractAsync(copy, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                onFault(ex);
            }
        }
    }

    /// <summary>Ends <see cref="RunAsync"/> once what is queued has been processed.</summary>
    public void Complete() => queue.Writer.TryComplete();

    async Task ExtractAsync(AcceptedCopy copy, CancellationToken ct)
    {
        var persona = LogSanitize.Strip(copy.PersonaName);
        if (!enabled.IsEnabled)
        {
            logger.LogDebug("Life extraction skipped for {Persona}: switched off", persona);
            return;
        }

        var decision = gate.Evaluate();
        if (!decision.Allowed)
        {
            logger.LogDebug("Life extraction skipped for {Persona}: {Reason}", persona, LogSanitize.Strip(decision.Reason));
            return;
        }

        var remaining = new Dictionary<long, int>();
        var room = await RoomAsync(copy.PersonaId, remaining, ct);
        if (room <= 0)
        {
            logger.LogDebug("Life extraction skipped for {Persona}: {Max} new rows already stored this window", persona, LifeMemoryRules.MaxNewPerWindow);
            return;
        }

        var known = await store.RecentLifeAsync(copy.PersonaId, LifeMemoryRules.KnownShown, ct);
        var reply = await llm.CompleteAsync(LifeExtractionPrompt.System, LifeExtractionPrompt.User(copy.Text, known), ct);
        if (reply is null)
            return;

        var claims = LifeClaimParser.Parse(reply);
        if (claims is not { Count: > 0 })
        {
            logger.LogDebug(
                "Life extraction for {Persona} found no claims ({Shape}); nothing stored",
                persona, claims is null ? "reply was not a JSON array" : "empty array");
            return;
        }

        var forgotten = await store.ForgottenLifeAsync(copy.PersonaId, LifeMemoryRules.ForgottenChecked, ct);
        var roster = await personas.GetAllAsync(ct);
        var accepted = Accept(claims, new LifeKnownClaims(known.Concat(forgotten), roster.Select(p => p.Name)), room, persona);
        foreach (var claim in accepted)
            await StoreAsync(copy, claim, roster, remaining, ct);
    }

    // Rows a persona may still gain this window; read once per pass, then counted down as the pass stores rows.
    async Task<int> RoomAsync(long personaId, Dictionary<long, int> remaining, CancellationToken ct)
    {
        if (!remaining.TryGetValue(personaId, out var room))
        {
            room = LifeMemoryRules.MaxNewPerWindow
                - await store.CountExtractedSinceAsync(personaId, time.GetUtcNow() - LifeMemoryRules.NewWindow, ct);
            remaining[personaId] = room;
        }

        return room;
    }

    // The model is not trusted: refuse vague, music and repeated claims, then keep only what the window has room for.
    List<ExtractedClaim> Accept(IReadOnlyList<ExtractedClaim> claims, LifeKnownClaims seen, int room, string persona)
    {
        var accepted = new List<ExtractedClaim>();
        var rejected = 0;
        foreach (var claim in claims)
        {
            if (accepted.Count == room)
                break;
            if (LifeClaimRules.Reject(claim.Claim, seen) is not null)
            {
                rejected++;
                continue;
            }

            seen.Add(claim.Claim);
            accepted.Add(claim);
        }

        if (rejected > 0)
            logger.LogDebug("Life extraction for {Persona} refused {Rejected} claim(s) as vague, music or already known", persona, rejected);
        return accepted;
    }

    async Task StoreAsync(
        AcceptedCopy copy, ExtractedClaim claim, IReadOnlyList<Persona> roster, Dictionary<long, int> remaining, CancellationToken ct)
    {
        var own = new NewLifeClaim(copy.PersonaId, claim.Claim);
        remaining[copy.PersonaId]--;
        if (PersonaNameMatch.Other(claim.About, copy.PersonaId, roster) is not { } other
            || await RoomAsync(other.Id, remaining, ct) <= 0)
        {
            await store.AddAsync(own, ct);
            return;
        }

        remaining[other.Id]--;
        var outing = new NewMemoryEvent(
            MemoryEventKinds.Outing, OutingPhrasing.Summary(copy.PersonaName, other.Name, claim.Claim), time.GetUtcNow());
        await store.AddOutingAsync(
            outing,
            [own, new NewLifeClaim(other.Id, OutingPhrasing.ForOther(claim.Claim, other.Name, copy.PersonaName))],
            ct);
    }
}
