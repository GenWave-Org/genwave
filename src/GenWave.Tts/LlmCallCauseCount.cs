namespace GenWave.Tts;

/// <summary>
/// One aggregated row of <see cref="LlmCallCauseCounters.Snapshot"/> (SPEC F139.2, STORY-353, PLAN
/// T330): how many calls landed on <see cref="Cause"/> for <see cref="Model"/>/<see cref="Kind"/>
/// within the rolling 24h window. This is the seam a LATER task (T334) reads to build the
/// <c>/api/llm-calls</c> counter summary and the red health tile's "dominant recent cause" line —
/// nothing here reaches either surface yet.
/// </summary>
public sealed record LlmCallCauseCount(LlmCallCause Cause, string Model, LlmCallKind Kind, int Count);
