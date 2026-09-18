namespace GenWave.Orchestration;

/// <summary>
/// How a dropped slot is reported when its render fails (SPEC F186.3) — a closed hierarchy of
/// exactly three cases, each declared in its own sibling file: <see cref="SilentDrop"/>,
/// <see cref="WarnDrop"/>, <see cref="WarnAndEventDrop"/>.
/// </summary>
public abstract record DropPolicy
{
    private protected DropPolicy() { }
}
