namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/personas/{id}/avatar/from-pack</c> (SPEC F128.5, STORY-333,
/// PLAN T295) — a DTO deliberately narrower than any store/domain type reaches this action's own
/// <c>[FromBody]</c> binder (the mass-assignment discipline every other write route in this codebase
/// already follows, e.g. <c>PersonaRequest</c>): a caller can name a pack and an item, nothing else.
/// </summary>
/// <param name="PackSlug">The installed <c>station.avatar_pack.slug</c> to copy from. Nullable —
/// an absent/blank field is a validation failure at the action, not a binder-level 400, so it can be
/// reported with the same <see cref="Microsoft.AspNetCore.Mvc.ProblemDetails"/> shape every other
/// field-level rejection in this controller uses.</param>
/// <param name="ItemName">The pack item's own <c>name</c> (scoped unique within that pack, db/37) to
/// copy. Nullable — see <see cref="PackSlug"/>'s own remarks.</param>
public sealed record PersonaAvatarFromPackRequest(string? PackSlug, string? ItemName);
