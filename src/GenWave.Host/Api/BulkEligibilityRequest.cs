namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/media/eligibility</c> (F3 bulk eligibility).
/// The <see cref="Filter"/> selects the same rows that <c>GET /api/media</c> would return
/// for equivalent query parameters so the operator can preview before committing.
/// </summary>
public sealed record BulkEligibilityRequest(
    /// <summary>The eligibility value to write to every matching row.</summary>
    bool Eligible,
    /// <summary>
    /// Filter criteria matching the GET /api/media query parameters.
    /// All fields are optional; an absent field means "no constraint on this column".
    /// </summary>
    BulkEligibilityFilter Filter);
