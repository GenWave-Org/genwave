namespace GenWave.Host.Api;

/// <summary>
/// The whole body of <c>GET /api/attributions</c> (SPEC F169.2, STORY-404, PLAN T419) — every
/// installed jingle-pack/font-pack/voice-pack's credits, grouped by kind. <see cref="Groups"/> lists
/// only kinds with at least one installed pack (STORY-404 AC2: an empty group never appears), in the
/// fixed order <c>jingle-pack</c>, <c>font-pack</c>, <c>voice-pack</c> — see
/// <see cref="AttributionsController"/>'s own remarks for why that order is fixed rather than
/// alphabetical or install-time.
/// </summary>
/// <param name="Groups">One entry per populated pack kind.</param>
public sealed record AttributionsResponse(IReadOnlyList<AttributionGroupDto> Groups);
