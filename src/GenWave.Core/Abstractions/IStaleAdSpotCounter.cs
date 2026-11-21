namespace GenWave.Core.Abstractions;

/// <summary>
/// How many ready ad spots are still in the old voice (SPEC F219.23): the backlog <see cref="IAdSpotStore.FindStaleReadyAsync"/>
/// feeds the re-render pass from, counted by the very same predicate, so the number reaches zero when every airable spot has been re-rendered
/// (a spot the pass gave up on stays counted: it really is still in the old voice).
/// </summary>
public interface IStaleAdSpotCounter
{
    Task<int> CountStaleReadyAsync(int currentMarker, CancellationToken ct);
}
