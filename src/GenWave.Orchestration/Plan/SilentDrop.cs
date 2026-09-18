namespace GenWave.Orchestration;

/// <summary>
/// The slot drops with no log line at all — today's assignment (PLAN T521, cross-referenced against
/// <c>Orchestrator.EnqueuePatterAsync</c>'s render-await loop, "every OTHER kind's drop stays the
/// pre-existing silent skip") for BackAnnounce, Crosstalk, StationId (both the pooled and templated
/// rung), Ad, TimeDate, and LeadIn.
/// </summary>
public sealed record SilentDrop : DropPolicy;
