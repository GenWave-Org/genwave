namespace GenWave.VoiceOff.Text;

/// <summary>The kind of station text a gauntlet line stands in for (SPEC F214.2).</summary>
internal enum GauntletClass
{
    LeadIn,
    BackAnnounce,
    StationId,
    Pronunciation,
    Emotive,
    NonVerbal,
    Crosstalk,
}
