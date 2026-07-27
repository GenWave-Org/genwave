using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// Operator override of the explicit classification (SPEC F95.3, F95.5, STORY-251, PLAN T115) — one
/// PUT endpoint over <see cref="IMediaExplicitOverride"/>, deliberately mirroring
/// <see cref="RatingController"/>'s never-play wire shape: idempotent, no <c>If-Match</c> (an
/// operator write beats everything by definition — nothing to conflict on), id-based reachability
/// with no <see cref="IStationScopeProvider"/> gating (the same F33.5 rationale
/// <see cref="RatingController"/> documents — classification is a per-row curation property, not a
/// rotation-scope one).
///
/// Kept as its own controller/interface rather than folded into <see cref="MediaController"/>/
/// <see cref="IAdminMediaWrite"/>: the same interface-segregation reasoning
/// <see cref="IAdminMediaWrite"/>'s own doc comment states for splitting query/lookup/write applies
/// again here — this one new method would otherwise force every existing <c>IAdminMediaWrite</c>
/// test double across Host.Tests to grow a stub it doesn't care about.
///
/// F95.5 (never-play orthogonality) needs no code in this controller at all: <c>never_play</c> lives
/// in a separate table behind <see cref="IMediaRating"/>, untouched by this write.
/// </summary>
[ApiController]
[Route("api")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Curation)]
public sealed class ExplicitOverrideController(IMediaExplicitOverride explicitOverride) : ControllerBase
{
    /// <summary>
    /// PUT /api/media/{id}/explicit — set or clear the operator's explicit-classification override
    /// (SPEC F95.3, F95.5). Body: <c>{ "explicit": true | false | null }</c>.
    ///   • true/false — stamps <c>explicit = &lt;value&gt;</c>, <c>explicit_source = 'operator'</c>.
    ///     Unconditional: an operator write beats any tag/LLM classification by definition and is
    ///     never overwritten by a later sweep (F95.3's precedence, operator &gt; tag &gt; llm — both
    ///     the tag pass and the LLM sweep defer to an existing operator stamp before writing).
    ///   • null (clear) — wipes <c>explicit</c>/<c>explicit_source</c>/<c>explicit_llm_missed_at</c>
    ///     back to NULL/NULL/NULL, releasing the row to the tag pass or the next LLM sweep tick.
    /// The <c>"explicit"</c> property is REQUIRED — a body that omits it (including <c>{}</c>) is a
    /// 400, never a silent clear (gh review fail-open finding: absence must never mean the same
    /// thing as an explicit JSON <c>null</c>, since a clear also wipes the LLM-sweep miss stamp and
    /// re-admits the row on an everyone station). Any value for <c>"explicit"</c> other than a JSON
    /// boolean or <c>null</c> (e.g. a string, number, array, object) is also a 400.
    /// Unknown id → 404. Idempotent — repeat sets/clears are safe no-ops, mirroring
    /// <see cref="RatingController.SetNeverPlay"/>; no <c>If-Match</c> concurrency machinery, same
    /// rationale as that endpoint (nothing to conflict on).
    /// </summary>
    [HttpPut("media/{id:long}/explicit")]
    [Consumes("application/json")]
    public async Task<IActionResult> SetExplicit(
        long id,
        [FromBody] JsonElement body,
        CancellationToken ct)
    {
        if (!TryParseExplicit(body, out var explicitValue))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Invalid \"explicit\" value.",
                Detail = "\"explicit\" is required and must be true, false, or null.",
            });
        }

        var outcome = await explicitOverride.SetExplicitOverrideAsync(id, explicitValue, ct);

        return outcome.Result switch
        {
            ExplicitOverrideResult.Updated  => Ok(new { @explicit = outcome.Explicit, explicitSource = outcome.ExplicitSource }),
            ExplicitOverrideResult.NotFound => NotFound(),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// Reads the tri-state wire value directly off the bound <see cref="JsonElement"/> rather than
    /// a typed DTO — a <c>bool?</c>-typed property cannot distinguish "the client omitted
    /// <c>explicit</c>" from "the client sent <c>explicit: null</c>" (model binding maps both to
    /// the same C# default), which is exactly how the fail-open regression this guards against
    /// shipped. Absent, or present with any <see cref="JsonValueKind"/> other than
    /// <see cref="JsonValueKind.True"/>/<see cref="JsonValueKind.False"/>/<see cref="JsonValueKind.Null"/>,
    /// is rejected.
    /// </summary>
    static bool TryParseExplicit(JsonElement body, out bool? explicitValue)
    {
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("explicit", out var prop))
        {
            switch (prop.ValueKind)
            {
                case JsonValueKind.True:
                    explicitValue = true;
                    return true;
                case JsonValueKind.False:
                    explicitValue = false;
                    return true;
                case JsonValueKind.Null:
                    explicitValue = null;
                    return true;
            }
        }

        explicitValue = null;
        return false;
    }
}
