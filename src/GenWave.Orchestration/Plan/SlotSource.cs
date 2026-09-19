namespace GenWave.Orchestration;

/// <summary>
/// Where a <see cref="PlannedSlot"/>'s content comes from (SPEC F187.1) — a closed hierarchy of
/// exactly three cases, each declared in its own sibling file: <see cref="RenderSource"/>,
/// <see cref="VerbatimSource"/>, <see cref="ReadySource"/>. The constructor is
/// <see langword="private protected"/> so only a case declared in this assembly or its <c>InternalsVisibleTo</c>
/// friend (the test project) can derive from it (SPEC F187.1's "closed hierarchy").
/// </summary>
public abstract record SlotSource
{
    private protected SlotSource() { }
}
