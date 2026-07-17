namespace GenWave.Host.Options;

/// <summary>
/// Safe-loop authoring config within the Station section (SPEC F27). These are
/// generation-time inputs consumed when a safe segment is rendered — not runtime tuning
/// knobs — and are deliberately excluded from the F19 live-settings allowlist (F27.10,
/// the F26 lesson: brand is not an ear-tuning knob). Bound to <c>Station:Safe</c>.
///
/// Read by <c>POST /api/safe-segments</c> (F27.3, P6); the boot seed (F27.6, P7) reads it too.
/// </summary>
public sealed class StationSafeOptions
{
    /// <summary>
    /// Template for the boot-seeded segment and the <c>POST /api/safe-segments</c> form
    /// pre-fill. <c>{StationName}</c> expands to <see cref="StationOptions.Name"/> (F27.6).
    /// </summary>
    public string SeedMessage { get; set; } =
        "You're listening to {StationName}. We'll be right back — stay tuned.";

    /// <summary>Writable volume root where authored segments land (F11.12, F27.1).</summary>
    public string AuthoredRoot { get; set; } = "/authored";

    /// <summary>Bed attenuation, in dB, relative to the voice in the offline mix (F27.4).</summary>
    public double BedDuckDb { get; set; } = -12.0;

    /// <summary>Bed lead-in/tail-out, in seconds, around the voice (F27.4).</summary>
    public double BedPadSeconds { get; set; } = 1.5;
}
