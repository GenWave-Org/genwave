using System.Text.Json;
using GenWave.Core.TalkRules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GenWave.Host.Api;

/// <summary>
/// Admin API for the DJ-line rules (SPEC F220.28: every route needs admin auth, as Settings). Today only the replay.
/// </summary>
[ApiController]
[Route("api/talk-rules")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class TalkRulesController(ITalkRuleReplay replay, TimeProvider timeProvider) : ControllerBase
{
    /// <summary>The widest window a replay may ask for, matching the longest <c>TalkRules:CheckRetentionDays</c>.</summary>
    public const int MaxDays = 90;

    private const string CompareLegacy = "legacy";

    /// <summary>
    /// POST /api/talk-rules/replay — re-judges the stored snapshots of the last <c>days</c> with today's rules (F220.26) and,
    /// with <c>compare: "legacy"</c>, lists where the rules and today's code disagree (F220.20). It never calls the LLM and never writes a row.
    /// </summary>
    [HttpPost("replay")]
    [Consumes("application/json")]
    public async Task<IActionResult> Replay([FromBody] TalkRulesReplayRequest request, CancellationToken ct)
    {
        if (request.Days is < 1 or > MaxDays)
        {
            return Invalid($"days must be between 1 and {MaxDays}.");
        }

        if (request.Compare is not null && !string.Equals(request.Compare, CompareLegacy, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid($"compare must be \"{CompareLegacy}\" or left out.");
        }

        if (!TryParseKinds(request.Kinds, out var kinds))
        {
            return Invalid("kinds may only hold \"dj-line\", \"chat\" and \"ad\".");
        }

        var since = timeProvider.GetUtcNow().AddDays(-request.Days);
        var report = await replay.ReplayAsync(new TalkReplayRequest(kinds, since, request.Compare is not null), ct);
        return Ok(new TalkRulesReplayResponse(
            report.Counts.Select(count => new TalkRuleCountDto(count.RuleId, count.Checked, count.Flagged)).ToList(),
            report.Changes.Select(ToDto).ToList(),
            report.TrimSavedLines));
    }

    private static bool TryParseKinds(IReadOnlyList<string>? requested, out TalkKinds kinds)
    {
        kinds = TalkKinds.DjLine | TalkKinds.Chat | TalkKinds.Ad;
        if (requested is null or { Count: 0 })
        {
            return true;
        }

        kinds = TalkKinds.None;
        foreach (var token in requested)
        {
            switch (token)
            {
                case "dj-line": kinds |= TalkKinds.DjLine; break;
                case "chat": kinds |= TalkKinds.Chat; break;
                case "ad": kinds |= TalkKinds.Ad; break;
                default: return false;
            }
        }

        return true;
    }

    private static TalkVerdictChangeDto ToDto(TalkVerdictChange change) =>
        new(
            change.CheckId,
            change.RuleId,
            ToDto(change.Stored),
            ToDto(change.Replayed),
            change.LegacyOutcome is { } outcome ? JsonNamingPolicy.KebabCaseLower.ConvertName(outcome.ToString()) : null);

    private static TalkRuleFlagDto? ToDto(RuleFlag? flag) =>
        flag is null ? null : new TalkRuleFlagDto(flag.Reason, flag.Ask, flag.Evidence);

    private ObjectResult Invalid(string detail) =>
        new(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Validation error.", Detail = detail })
        {
            StatusCode = StatusCodes.Status400BadRequest,
        };
}
