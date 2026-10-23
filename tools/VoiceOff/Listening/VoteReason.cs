namespace GenWave.VoiceOff.Listening;

/// <summary>The fixed list of reasons a judge may give for a vote (SPEC F214.10).</summary>
internal enum VoteReason
{
    Robotic,
    Pace,
    Pronunciation,
    Flat,
    Breathy,
    Glitch,
    Other,
}
