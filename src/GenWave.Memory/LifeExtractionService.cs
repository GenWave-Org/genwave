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
/// </summary>
public sealed class LifeExtractionService(
    ILifeClaimLlm llm,
    ILifeStore store,
    IPersonaStore personas,
    ILlmBatchGate gate,
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
        var decision = gate.Evaluate();
        if (!decision.Allowed)
        {
            logger.LogDebug("Life extraction skipped for {Persona}: {Reason}", LogSanitize.Strip(copy.PersonaName), LogSanitize.Strip(decision.Reason));
            return;
        }

        var reply = await llm.CompleteAsync(LifeExtractionPrompt.System, LifeExtractionPrompt.User(copy.Text), ct);
        if (reply is null)
            return;

        var claims = LifeClaimParser.Parse(reply);
        if (claims is not { Count: > 0 })
        {
            logger.LogDebug(
                "Life extraction for {Persona} found no claims ({Shape}); nothing stored",
                LogSanitize.Strip(copy.PersonaName), claims is null ? "reply was not a JSON array" : "empty array");
            return;
        }

        var roster = await personas.GetAllAsync(ct);
        foreach (var claim in claims)
            await StoreAsync(copy, claim, roster, ct);
    }

    Task StoreAsync(AcceptedCopy copy, ExtractedClaim claim, IReadOnlyList<Persona> roster, CancellationToken ct)
    {
        var own = new NewLifeClaim(copy.PersonaId, claim.Claim);
        if (PersonaNameMatch.Other(claim.About, copy.PersonaId, roster) is not { } other)
            return store.AddAsync(own, ct);

        var outing = new NewMemoryEvent(
            MemoryEventKinds.Outing, OutingPhrasing.Summary(copy.PersonaName, other.Name, claim.Claim), time.GetUtcNow());
        return store.AddOutingAsync(
            outing,
            [own, new NewLifeClaim(other.Id, OutingPhrasing.ForOther(claim.Claim, other.Name, copy.PersonaName))],
            ct);
    }
}
