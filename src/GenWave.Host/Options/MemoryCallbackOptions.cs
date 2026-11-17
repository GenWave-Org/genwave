namespace GenWave.Host.Options;

/// <summary>
/// The callback rate (SPEC F218.41, STORY-548). Bound to the <c>Memory</c> section so the setting
/// <c>Memory:CallbacksPerHour</c> lands on <see cref="CallbacksPerHour"/>; the settings validator holds it to 0-6.
/// </summary>
public sealed class MemoryCallbackOptions
{
    public const string Section = "Memory";

    /// <summary>Callback breaks per DJ per hour: 1 by default, 0 turns them off.</summary>
    public int CallbacksPerHour { get; set; } = 1;
}
