using GenWave.Plugins;

namespace GenWave.Host.Api;

/// <summary>
/// Boot-time plugin-load outcomes, read by <see cref="StatusController"/> (SPEC F156.7, STORY-386
/// AC2/AC3) and <c>PluginDoorNarrationExtensions.NarratePluginDoor</c> (the booth-log/ILogger
/// narration, PLAN T394) — a plain concrete singleton, self-registered by its own type, mirroring
/// <see cref="ProcessStartTime"/>'s own shape rather than a <c>GenWave.*</c> interface: SPEC F156.8's
/// closed-door SEAMS-inertness rule means the composition root must register the SAME graph whether
/// the door is open or shut, and a <c>GenWave.*</c> interface-typed port here would show up in
/// SEAMS.md as a NEW, always-registered seam either way — an accounting change this task's own
/// byte-identical-SEAMS proof has no reason to force. A concrete class earns no row there at all
/// (<c>SeamIndexDocument.IsGenWavePort</c> only ever looks at interfaces), so this accessor's mere
/// existence never touches SEAMS.md regardless of what the door decides.
///
/// <para>
/// Starts empty (<see cref="Reports"/> is an empty list, <see cref="MissingKnobNote"/> is null) — the
/// exact state a closed door, or a boot before the plugin-door wiring below ever runs, leaves it in.
/// Populated at most once per boot, synchronously, by <c>PluginDoorServiceCollectionExtensions
/// .AddGenWavePluginDoor</c> — before <c>builder.Build()</c> ever returns — and read only afterward;
/// nothing else in this codebase ever calls <see cref="Record"/>/<see cref="RecordMissingKnob"/>.
/// </para>
/// </summary>
public sealed class PluginStatusAccessor
{
    /// <summary>Every plugin outcome the loader produced, loaded or skipped, in the same order
    /// <c>PluginLoader.LoadAll</c> returned them — empty when the door never ran (closed, or only one
    /// knob present).</summary>
    public IReadOnlyList<PluginLoadReport> Reports { get; private set; } = Array.Empty<PluginLoadReport>();

    /// <summary>
    /// Set only when exactly one of the two boot knobs (<c>Plugins:Enabled</c>, <c>Plugins:Root</c>)
    /// was present, naming the missing half (SPEC F156.1) — null in every other case (both knobs
    /// present and the loader ran, or neither knob was set at all, STORY-385 AC1).
    /// </summary>
    public string? MissingKnobNote { get; private set; }

    public void Record(IReadOnlyList<PluginLoadReport> reports) => Reports = reports;

    public void RecordMissingKnob(string note) => MissingKnobNote = note;
}
