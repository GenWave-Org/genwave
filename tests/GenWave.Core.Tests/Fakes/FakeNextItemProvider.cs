using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Fakes;

/// <summary>
/// Scripts the selection seam (PRD §4.1, SEAM 1) for feeder tests: returns the queued items in order,
/// then null (nothing available). Records each <see cref="PlayoutContext"/> so tests can assert the
/// feeder passes its recently-aired ids through for repeat-avoidance.
/// </summary>
sealed class FakeNextItemProvider : INextItemProvider
{
    readonly Queue<MediaItem?> items;

    public List<PlayoutContext> Calls { get; } = [];

    public FakeNextItemProvider(params MediaItem?[] items) => this.items = new Queue<MediaItem?>(items);

    public Task<MediaItem?> GetNextAsync(PlayoutContext ctx, CancellationToken ct)
    {
        Calls.Add(ctx);
        var next = items.Count > 0 ? items.Dequeue() : null;
        return Task.FromResult(next);
    }
}
