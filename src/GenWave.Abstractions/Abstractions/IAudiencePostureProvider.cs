using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F95.1/F95.4 (STORY-250, PLAN T111/T114) — the live station audience posture accessor every
/// pool-predicate query consults. The mirror of <see cref="ISafeScopeProvider"/> for
/// <c>Station:Audience</c>, and the same contract: implementations MUST re-evaluate
/// <see cref="Current"/> on every read — never cache the result in a field — so a live
/// <c>Station:Audience</c> edit governs the very next candidate-pool query with no api restart
/// (SPEC F95.6's end-to-end guarantee).
/// </summary>
public interface IAudiencePostureProvider
{
    /// <summary>The station's current audience posture, evaluated fresh on every call.</summary>
    AudiencePosture Current { get; }
}
