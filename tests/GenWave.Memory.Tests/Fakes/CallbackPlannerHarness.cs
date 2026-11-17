using GenWave.Core.Domain;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Memory.Tests.Fakes;

/// <summary>A <see cref="CallbackPlanner"/> over the in-memory store on a fake clock and a fixed draw.</summary>
public sealed class CallbackPlannerHarness(double draw = 0.1)
{
    public const long Persona = 7;

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 17, 18, 0, 0, TimeSpan.Zero));

    public FixedDraw Draw { get; } = new(draw);

    public InMemoryCallbackStore Store => store ??= new InMemoryCallbackStore(Time);

    public CallbackPlanner Planner => new(Store, Draw);

    InMemoryCallbackStore? store;

    public Task<CallbackAsk?> PlanAsync(int perHour = 1, SegmentKind kind = SegmentKind.LeadIn, params string[] exclusions) =>
        Planner.PlanAsync(new CallbackRequest(Persona, kind, perHour, exclusions), CancellationToken.None);

    public InMemoryCallbackStore.Memory Seed(string content = "has a dog named Daisy", string kind = "life", TimeSpan? age = null) =>
        Store.Seed(Persona, kind, content, age);
}
