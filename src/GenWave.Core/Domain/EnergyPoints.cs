namespace GenWave.Core.Domain;

/// <summary>
/// Intro and outro energy levels for a media file (STORY-029, Epic H). Measured once at library
/// scan; applied by the playout engine to drive crossfade depth decisions.
/// Both values are normalized to [0, 1] where 0.0 is silence and 1.0 is full peak energy.
/// </summary>
/// <param name="IntroEnergy">Normalized [0, 1] energy of the track's intro window.</param>
/// <param name="OutroEnergy">Normalized [0, 1] energy of the track's outro window.</param>
public sealed record EnergyPoints(double IntroEnergy, double OutroEnergy);
