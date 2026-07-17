namespace GenWave.Host.Options;

/// <summary>
/// Active-persona pointer within the Station config section (SPEC F35.2, F36.2). Bound to
/// <c>Station:Persona</c>; live-editable via the F19 settings overlay (joins the allowlist in
/// <see cref="Configuration.StationSettingsAllowlist"/>).
/// </summary>
public sealed class StationPersonaOptions
{
    /// <summary>
    /// The active persona's id, or <c>0</c> for "no persona" (neutral house style +
    /// <see cref="StationOptions.Voice"/>). A value with no matching <c>station.persona</c> row is
    /// legal — <c>IActivePersonaAccessor</c> consumers degrade to persona-less with a WARN rather
    /// than failing (F35.5); this class only guards non-negativity
    /// (<see cref="StationOptionsValidator"/>).
    /// </summary>
    public long ActiveId { get; set; }
}
