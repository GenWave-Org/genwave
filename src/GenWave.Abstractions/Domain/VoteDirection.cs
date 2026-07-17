namespace GenWave.Core.Domain;

/// <summary>
/// Direction of an operator vote on a track's rating (SPEC F33.3). Maps 1:1 to the
/// <c>POST /api/media/{id}/vote</c> request body's <c>direction</c> field (<c>"up"</c> / <c>"down"</c>);
/// mapping the raw string to this enum — and rejecting anything else with a 400 — is the controller's job.
/// </summary>
public enum VoteDirection
{
    Up,
    Down,
}
