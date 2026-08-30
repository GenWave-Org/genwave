namespace GenWave.Core.Domain;

/// <summary>
/// A show's most recent airing, read off <c>station.booth_log</c> (SPEC F152.5, STORY-373, PLAN
/// T362) — <see cref="Picks"/> is every <c>"track-started"</c> row in that airing's contiguous run
/// (see <see cref="Abstractions.IBoothLogReader.GetLastAiringAsync"/>'s own remarks for exactly what
/// "contiguous run" means), <see cref="Relaxed"/> is the subset of those rows whose stamped
/// <c>BoothLogPickStamp.RotationRelax</c> was greater than zero — the F152.4 relax ladder having had
/// to reach past R0 to keep the block from going silent. <see cref="Relaxed"/> is always at most
/// <see cref="Picks"/>.
/// </summary>
public sealed record ShowLastAiring(int Picks, int Relaxed);
