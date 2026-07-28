using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Fakes;

/// <summary>
/// A selection seam that blocks until the test releases it (gh-#184) — stands in for the live
/// Orchestrator awaiting patter LLM+TTS renders inside a refill. <see cref="Entered"/> completes
/// the moment the feeder first awaits the seam; queued items flow only after <see cref="Release"/>.
/// </summary>
sealed class BlockingNextItemProvider(params MediaItem?[] items) : INextItemProvider
{
    readonly Queue<MediaItem?> queue = new(items);
    readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the feeder first pulls the seam — i.e. the refill is now blocked.</summary>
    public Task Entered => entered.Task;

    /// <summary>Opens the gate: every queued item flows from here on.</summary>
    public void Release() => gate.TrySetResult();

    public async Task<MediaItem?> GetNextAsync(PlayoutContext ctx, CancellationToken ct)
    {
        entered.TrySetResult();
        await gate.Task.WaitAsync(ct);
        return queue.Count > 0 ? queue.Dequeue() : null;
    }
}
