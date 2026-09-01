using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F158.1/F158.2 (STORY-384, gh-#417, gh-#380 epic) — the ads seam: a source of pre-rendered
/// spots vended at the <c>Station:Ads:EveryNUnits</c> cadence (F158.3). Sources form a pipeline, not a
/// slot (the <see cref="IContextProvider"/> shape): <c>AdSpotPipeline</c> tries every registered source
/// in registration order and takes the first non-null answer; a source that throws is WARN-logged and
/// skipped, the same skip-never-silence handling <see cref="IContextProvider.FetchAsync"/> already
/// gets. Home's own library-backed source always registers last, so a plugin source can win the break
/// without ever replacing anything.
/// </summary>
public interface IAdSpotSource
{
    /// <summary>
    /// The next spot to air, or null when this source has nothing this break — <b>always a legal
    /// answer, never an error</b>. An empty ads library is a normal day: a null return here logs one
    /// INFO line at the pipeline, never a WARN. The returned <see cref="MediaItem"/> is vended
    /// straight onto the queue with no render at air time — this method itself does whatever rendering
    /// or lookup it needs before returning.
    /// </summary>
    /// <param name="ct">Propagated to any I/O this source performs while choosing a spot.</param>
    ValueTask<MediaItem?> GetNextSpotAsync(CancellationToken ct);
}
