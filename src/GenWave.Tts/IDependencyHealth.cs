namespace GenWave.Tts;

/// <summary>
/// Synchronous read seam over cached dependency-health verdicts (SPEC F70.2, STORY-187). Every
/// implementation is a plain in-memory lookup — no implementation may probe a dependency inside
/// <see cref="GetVerdict"/> — so a render-time engine-or-fallback decision (T34) can call this
/// from inside the 30s render window with zero network calls (STORY-187 AC2).
/// <para>
/// Verdicts are written on a completely separate cadence, by
/// <c>GenWave.Host.Health.DependencyHealthProbeService</c> driving a <c>DependencyHealthProber</c>
/// in the background. This seam has no write member: no on-air/render code path ever decides
/// "am I healthy" for itself — it only ever reads someone else's already-cached answer.
/// </para>
/// </summary>
public interface IDependencyHealth
{
    /// <summary>
    /// The most recent verdict for <paramref name="dependencyName"/> (see
    /// <see cref="DependencyNames"/>), or null if no probe cycle has completed for it yet — e.g.
    /// the brief startup window before the first cycle finishes.
    /// </summary>
    DependencyHealthVerdict? GetVerdict(string dependencyName);
}
