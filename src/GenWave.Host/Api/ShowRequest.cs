namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/shows</c> (create) and <c>PATCH /api/shows/{slug}</c> (edit) (SPEC
/// F115.1, F115.4). <see cref="Name"/> is required, non-blank — <see cref="Core.Abstractions.IShowStore"/>'s
/// own <see cref="Core.Domain.ShowWriteResult.InvalidName"/> rejects a blank/whitespace one, or one
/// whose derived slug equals the reserved fallback literal; <see cref="Tagline"/>/<see cref="Flavor"/>
/// are optional and clear to <c>null</c> when omitted or blank. All fields are nullable here, mirroring
/// <see cref="PersonaRequest"/>'s own all-nullable shape, so the controller produces a typed 400 for a
/// blank/missing name instead of an ASP.NET model-binder 400.
/// </summary>
/// <param name="SponsorId">SPEC F175.1 (STORY-430, PLAN T449) — a full replace, the same posture as
/// <see cref="Tagline"/>/<see cref="Flavor"/>: omitted or explicit <c>null</c> both clear any sponsor the
/// show currently carries. A non-null value that names no sponsor is 404 <c>sponsor_not_found</c>
/// (<see cref="ShowsController.Create"/>/<see cref="ShowsController.Update"/>), checked before either
/// write so a dangling id never surfaces as an unhandled foreign-key violation.</param>
public sealed record ShowRequest(string? Name, string? Tagline, string? Flavor, long? SponsorId = null);
