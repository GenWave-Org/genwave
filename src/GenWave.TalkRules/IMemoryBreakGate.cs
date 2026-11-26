using GenWave.Core.TalkRules;

namespace GenWave.TalkRules;

/// <summary>The "memory is on for this break" test the two memory shapers share; a service seam so a rule takes it by interface (F220.2).</summary>
public interface IMemoryBreakGate
{
    /// <summary>The break to offer memory for, or null when memory plays no part in it.</summary>
    Task<MemoryBreak?> OpenAsync(TalkSnapshot before, CancellationToken ct);
}
