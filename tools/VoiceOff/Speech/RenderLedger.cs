using GenWave.VoiceOff.Script;

namespace GenWave.VoiceOff.Speech;

/// <summary>
/// The renders recorded for one engine. A render of a line replaces the earlier one for the same
/// register, line and lane, and <see cref="Retain"/> drops renders of a voice the profile no longer uses, so a
/// re-pick replaces and never joins (SPEC F214.5).
/// </summary>
internal sealed class RenderLedger
{
    private readonly Dictionary<(GauntletRegister, string, RenderLane), RenderRecord> records = [];

    internal IReadOnlyCollection<RenderRecord> Records => records.Values;

    internal void Record(RenderRecord render)
    {
        ArgumentNullException.ThrowIfNull(render);
        records[(render.Register, render.LineId, render.Lane)] = render;
    }

    /// <summary>Keeps only the renders made with each register's current voice.</summary>
    internal void Retain(EngineProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        foreach (KeyValuePair<(GauntletRegister Register, string LineId, RenderLane Lane), RenderRecord> entry in records.ToList())
        {
            if (!string.Equals(entry.Value.Voice, profile.VoiceFor(entry.Key.Register), StringComparison.Ordinal))
            {
                records.Remove(entry.Key);
            }
        }
    }
}
