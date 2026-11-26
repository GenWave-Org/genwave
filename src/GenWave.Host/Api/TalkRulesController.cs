using System.Text.Json;
using GenWave.Core.TalkRules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GenWave.Host.Api;

/// <summary>
/// Admin API for the DJ-line rules (SPEC F220.22-F220.28: every route needs admin auth, as Settings): the list, a switch, the flagged attempts and the replay.
/// </summary>
[ApiController]
[Route("api/talk-rules")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class TalkRulesController(
    ITalkRuleReplay replay,
    ITalkRuleBook book,
    ITalkRuleSwitchboard switchboard,
    ITalkFlagReader flags,
    TimeProvider timeProvider) : ControllerBase
{
    /// <summary>The widest window a replay may ask for, matching the longest <c>TalkRules:CheckRetentionDays</c>.</summary>
    public const int MaxDays = 90;

    /// <summary>How many flagged attempts <c>GET {id}/flags</c> returns.</summary>
    public const int FlagsPageSize = 20;

    private const string CompareLegacy = "legacy";
    private const int ListFlagsDays = 7;

    /// <summary>GET /api/talk-rules — every rule: how it explains itself, each kind's switch (defaults applied) and last week's flags and could-not-run counts per kind (F220.24, F220.9).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TalkRuleDto>>> List(CancellationToken ct)
    {
        var switchesByKind = new Dictionary<TalkKinds, IReadOnlyDictionary<string, bool>>();
        foreach (var kind in TalkKindSlugs.All)
        {
            switchesByKind[kind] = await book.SwitchesAsync(kind, ct);
        }

        var since = timeProvider.GetUtcNow().AddDays(-ListFlagsDays);
        var counts = (await flags.CountVerdictsAsync(since, ct)).ToDictionary(count => (count.RuleId, count.Kind));

        var rules = book.Rules.Select(rule =>
        {
            var covered = TalkKindSlugs.All.Where(kind => rule.CanCover.HasFlag(kind)).ToList();
            var description = rule.Describe();
            return new TalkRuleDto(
                rule.Id,
                description.Name,
                description.WhatItDoes,
                description.Why,
                description.Example,
                covered.Select(TalkKindSlugs.Of).ToList(),
                covered.ToDictionary(
                    TalkKindSlugs.Of,
                    kind => switchesByKind[kind][rule.Id]),
                covered.ToDictionary(TalkKindSlugs.Of, kind => counts.GetValueOrDefault((rule.Id, kind))?.Flagged ?? 0),
                covered.ToDictionary(TalkKindSlugs.Of, kind => counts.GetValueOrDefault((rule.Id, kind))?.Broken ?? 0));
        }).ToList();

        return Ok(rules);
    }

    /// <summary>
    /// PUT /api/talk-rules/{id}/{kind} <c>{ "enabled": bool }</c> — switches one rule for one kind of talk, live (F220.22, F220.23).
    /// 404 for an unknown rule, 400 for an unknown kind or a kind the rule cannot cover; the switchboard makes both rule checks.
    /// </summary>
    [HttpPut("{id}/{kind}")]
    [Consumes("application/json")]
    public async Task<IActionResult> Switch(string id, string kind, [FromBody] TalkRuleSwitchRequest request, CancellationToken ct)
    {
        if (request.Enabled is not { } enabled)
        {
            return Invalid("enabled must be true or false.");
        }

        if (!TalkKindSlugs.TryParse(kind, out var talkKind))
        {
            return Invalid("kind must be \"dj-line\", \"chat\" or \"ad\".");
        }

        return await switchboard.SwitchAsync(id, talkKind, enabled, ct) switch
        {
            TalkRuleSwitchResult.Stored => Ok(new TalkRuleSwitchResponse(id, TalkKindSlugs.Of(talkKind), enabled)),
            TalkRuleSwitchResult.UnknownRule => NotFound(),
            TalkRuleSwitchResult.KindNotCovered => Invalid("That rule cannot cover that kind of talk."),
            var other => throw new InvalidOperationException($"Unhandled switch result {other}."),
        };
    }

    /// <summary>GET /api/talk-rules/{id}/flags?days=7 — the latest 20 attempts the rule flagged, newest first (F220.25).</summary>
    [HttpGet("{id}/flags")]
    public async Task<ActionResult<IReadOnlyList<TalkFlaggedAttemptDto>>> Flags(string id, [FromQuery] int days = ListFlagsDays, CancellationToken ct = default)
    {
        if (days is < 1 or > MaxDays)
        {
            return Invalid($"days must be between 1 and {MaxDays}.");
        }

        if (!book.Rules.Any(rule => string.Equals(rule.Id, id, StringComparison.Ordinal)))
        {
            return NotFound();
        }

        var since = timeProvider.GetUtcNow().AddDays(-days);
        var attempts = await flags.LatestFlagsAsync(id, since, FlagsPageSize, ct);
        return Ok(attempts.Select(attempt => new TalkFlaggedAttemptDto(
            attempt.CheckId,
            attempt.CheckedAt,
            attempt.Dj,
            TalkKindSlugs.Of(attempt.Kind),
            attempt.Words,
            OutcomeToken(attempt.Outcome))).ToList());
    }

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
            if (!TalkKindSlugs.TryParse(token, out var kind))
            {
                return false;
            }

            kinds |= kind;
        }

        return true;
    }

    private static TalkVerdictChangeDto ToDto(TalkVerdictChange change) =>
        new(
            change.CheckId,
            change.RuleId,
            ToDto(change.Stored),
            ToDto(change.Replayed),
            change.LegacyOutcome is { } outcome ? OutcomeToken(outcome) : null);

    private static string OutcomeToken<TOutcome>(TOutcome outcome)
        where TOutcome : struct, Enum =>
        JsonNamingPolicy.KebabCaseLower.ConvertName(outcome.ToString());

    private static TalkRuleFlagDto? ToDto(RuleFlag? flag) =>
        flag is null ? null : new TalkRuleFlagDto(flag.Reason, flag.Ask, flag.Evidence);

    private ObjectResult Invalid(string detail) =>
        new(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Validation error.", Detail = detail })
        {
            StatusCode = StatusCodes.Status400BadRequest,
        };
}
