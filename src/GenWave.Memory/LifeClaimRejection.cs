namespace GenWave.Memory;

/// <summary>Why <see cref="LifeClaimRules"/> refused an extracted claim (STORY-537, PLAN T704a).</summary>
public enum LifeClaimRejection
{
    /// <summary>Too short, or only a bare possession ("has a pet"): nothing to remember.</summary>
    Vague,

    /// <summary>About playing or listening to music, a song, a track, an album, a playlist or an artist: not the DJ's life.</summary>
    TrackTalk,

    /// <summary>Says what a known detail, or an earlier claim in the same reply, already says.</summary>
    NearDuplicate,
}
