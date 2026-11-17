namespace GenWave.Core.Abstractions;

/// <summary>
/// The operator's live callback rate (<c>Memory:CallbacksPerHour</c>, SPEC F218.41): about how many callback breaks
/// a DJ airs per hour. Read per break, so a change applies to the very next one with no restart.
/// </summary>
public interface ICallbackRate
{
    /// <summary>0 turns callbacks off; the planner treats anything above 6 as 6.</summary>
    int CallbacksPerHour { get; }
}
