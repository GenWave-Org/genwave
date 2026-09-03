namespace GenWave.Host.Api;

/// <summary>
/// <c>PATCH /api/ad-briefs/{id}</c>'s own request body (SPEC F162.1; STORY-392; PLAN T403b) — the
/// toggle's only field. Deliberately not the <c>AdSpotSaveRequest</c>/<c>MediaController.Patch</c>
/// sparse-edit shape (many nullable fields, "null = leave unchanged"): a brief's only PATCH surface is
/// <see cref="Enabled"/>, so <see langword="null"/> here means "missing", refused as a 400 rather than
/// read as "leave unchanged" — see <see cref="AdBriefsController.SetEnabled"/>'s own remarks for why
/// this endpoint carries no If-Match ceremony either.
/// </summary>
/// <param name="Enabled">The brief's new enabled state — required.</param>
public sealed record AdBriefPatchRequest(bool? Enabled);
