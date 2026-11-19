namespace GenWave.Ads;

using GenWave.Tts;

/// <summary>
/// A spot's cast resolved for the active engine (SPEC F219.8, F219.9). <paramref name="MissingSettings"/> names
/// what is behind every speaker the script uses that has no voice on the active engine (distinct, in cast order):
/// a setting key, or the spot's own voice cast when no setting holds its voice; an empty list means
/// <paramref name="Cast"/> is complete and the spot can render.
/// </summary>
public sealed record AdCastResolution(IReadOnlyList<CastMember> Cast, IReadOnlyList<string> MissingSettings);
