using GenWave.Core.Domain;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Memory.Tests.Fakes;

/// <summary>
/// The life extractor wired to fakes: a roster of Sal (1) and Captain Flip (2), an in-memory life store and a
/// canned model reply, all on one <see cref="Time"/>. <see cref="RunAsync"/> queues one accepted copy and drains it
/// as the Host's loop would.
/// </summary>
public sealed class LifeHarness
{
    public const long Sal = 1;
    public const long Flip = 2;

    readonly bool gateAllows;

    public LifeHarness(string? reply, bool gateAllows = true, bool extractionOn = true)
    {
        this.gateAllows = gateAllows;
        Llm = new FakeLifeClaimLlm(reply);
        Switch = new FakeLifeExtractionSwitch(extractionOn);
        Store = new InMemoryLifeStore(Time);
    }

    public FakeTimeProvider Time { get; } = new();

    public InMemoryLifeStore Store { get; }

    public FakeLifeClaimLlm Llm { get; }

    public FakeLifeExtractionSwitch Switch { get; }

    public CapturingLogger<LifeExtractionService> Log { get; } = new();

    public async Task RunAsync(string copy, long personaId = Sal, string personaName = "Sal")
    {
        var service = new LifeExtractionService(
            Llm, Store, new FakePersonaRoster((Sal, "Sal"), (Flip, "Captain Flip")), new FakeLlmBatchGate(gateAllows),
            Switch, Time, Log);
        service.Enqueue(new AcceptedCopy(personaId, personaName, copy));
        service.Complete();
        await service.RunAsync(ex => throw ex, CancellationToken.None);
    }
}
