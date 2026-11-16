namespace GenWave.Core.Domain;

/// <summary>The <c>memory_event.kind</c> values the shared-happening log carries (SPEC F218.15).</summary>
public static class MemoryEventKinds
{
    /// <summary>A crosstalk two personas had on air.</summary>
    public const string Crosstalk = "crosstalk";

    /// <summary>One persona handing the station over to another at a boundary (SPEC F218.15(b)).</summary>
    public const string Handoff = "handoff";

    /// <summary>Something two personas did together off air, extracted from a copy (SPEC F218.18).</summary>
    public const string Outing = "outing";
}
