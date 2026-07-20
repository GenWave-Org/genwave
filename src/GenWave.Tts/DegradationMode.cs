namespace GenWave.Tts;

/// <summary>
/// The LLM degradation ladder (SPEC F69.1, STORY-188): three rungs, always one step at a time
/// (<see cref="DegradationController"/>), read by <see cref="DegradationGatedCopyWriter"/> to
/// decide whether a playout segment attempts the LLM at all.
/// </summary>
public enum DegradationMode
{
    /// <summary>Full path: <see cref="LlmCopyWriter"/> attempts every LeadIn/BackAnnounce.</summary>
    Normal,

    /// <summary>
    /// Cheap/canned copy path (SPEC F69.1): playout routes to <see cref="TemplateCopyWriter"/> for
    /// almost every segment, with one real LLM attempt permitted per
    /// <see cref="DegradationOptions.CooldownSeconds"/> window (<see cref="DegradationGatedCopyWriter"/>'s
    /// cadence) — "minimized calls", not zero. One drop below <see cref="Normal"/> — reached after
    /// the first threshold's worth of consecutive call failures. NOT a one-way trap: a sustained
    /// outage still auto-drops Soft → <see cref="Hard"/>, either via that throttled real attempt
    /// failing enough times, or via the independent background dependency probe reporting unhealthy
    /// past cooldown — see <see cref="DegradationController"/>'s own remarks.
    /// </summary>
    Soft,

    /// <summary>
    /// Zero LLM calls (SPEC F69.1) — the one mode with no cadence exception at all, unlike
    /// <see cref="Soft"/>. Also the state <see cref="DegradationController"/> reports while the LLM
    /// is unconfigured by design (empty <c>Llm:Endpoint</c>) — see its remarks for that distinction.
    /// </summary>
    Hard,
}
