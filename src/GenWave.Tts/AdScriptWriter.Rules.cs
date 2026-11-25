namespace GenWave.Tts;

using System.Diagnostics;
using Microsoft.Extensions.Logging;

/// <summary>
/// The rules-mode path of <see cref="AdScriptWriter"/> (SPEC F220.10-F220.16, PLAN T802). The writer keeps what it owns: the
/// HTTP call, the length and empty checks, the line-aware hygiene (no phone rewrite), and every ring entry. <see cref="IAdRulesFlow"/>
/// owns the order of the rest. Legacy and the shadow hook live in the other file and are not reached from here.
/// </summary>
public sealed partial class AdScriptWriter
{
    async Task<AdScriptWriteResult> WriteWithRulesAsync(
        IAdRulesFlow rulesFlow, HttpClient http, Uri requestUri, LlmOptions cfg, AdScriptWriteRequest request,
        string systemPrompt, string userPrompt, int maxTokens, DegradationMode mode, CancellationToken ct)
    {
        // The ring entry of an attempt reads the user prompt and the start time of the call it describes, so a re-ask moves both.
        var askedPrompt = userPrompt;
        var startedAt = timeProvider.GetUtcNow();

        // Each attempt keeps its own Llm:TimeoutSeconds budget, as the legacy re-ask does (gh-#696).
        async Task<AdRulesReply> AskAsync(int attempt, string prompt)
        {
            askedPrompt = prompt;
            startedAt = timeProvider.GetUtcNow();
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(cfg.TimeoutSeconds), timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
            return await AskForRulesAsync(
                http, requestUri, cfg, request, systemPrompt, prompt, maxTokens, mode, startedAt, ct, linked.Token);
        }

        // The caught first call gets its own honest ring entry BEFORE the re-ask fires (legacy's own order).
        void RecordCaught(string caughtRaw) =>
            recorder.Record(
                request.SponsorName, systemPrompt, askedPrompt, caughtRaw, startedAt, ElapsedMs(startedAt),
                LlmCallOutcome.Ok, statusDetail: null, mode, LlmCallCause.RuleFlagged, cfg.Model, LlmCallKind.AdScript);

        var result = await rulesFlow.RunAsync(request, userPrompt, new AdRulesCalls(AskAsync, RecordCaught), ct);
        switch (result)
        {
            case AdRulesResult.Faulted faulted:
                return faulted.Result;
            case AdRulesResult.ShapeRefused refused:
                return Failed(
                    BoundReason(refused.Reason), refused.RuleId, MapRuleIdToCause(refused.RuleId), request.SponsorName, startedAt, mode,
                    systemPrompt, askedPrompt, cfg.Model, refused.Raw);
            case AdRulesResult.StillFlagged flagged:
                return Failed(
                    flagged.Reason, AdRulesResult.StillFlagged.FailedRuleId, LlmCallCause.RuleFlagged, request.SponsorName, startedAt, mode, systemPrompt,
                    askedPrompt, cfg.Model, flagged.Raw);
            case AdRulesResult.Accepted accepted:
                // The call is recorded as accepted first, then the recording rules hear of it and the attempt's row is written.
                var success = Accept(
                    accepted.Script, request.SponsorName, systemPrompt, askedPrompt, accepted.Raw, startedAt, mode, cfg.Model);
                await rulesFlow.AcceptedAsync(accepted.Pending, ct);
                return success;
            default:
                throw new UnreachableException($"Unhandled {nameof(AdRulesResult)} case.");
        }
    }

    /// <summary>
    /// One completion and the fixed clean-up up to the shape checks: the length check, the line-aware hygiene, and the empty check.
    /// A fault is recorded in the ring and returned on the reply, never thrown, except the caller's own cancellation.
    /// </summary>
    async Task<AdRulesReply> AskForRulesAsync(
        HttpClient http, Uri requestUri, LlmOptions cfg, AdScriptWriteRequest request, string systemPrompt, string userPrompt,
        int maxTokens, DegradationMode mode, DateTimeOffset startedAt, CancellationToken ct, CancellationToken linkedCt)
    {
        var raw = string.Empty;
        try
        {
            var reply = await LlmCopyWriter.PostChatCompletionAsync(
                http, requestUri, cfg, systemPrompt, userPrompt, maxTokens, linkedCt);
            raw = reply.Content;

            if (reply.FinishReason == "length")
            {
                return new AdRulesReply(raw, null, Failed(
                    "the completion was cut short by max_tokens (finish_reason: length) — a truncated script is never aired",
                    ruleId: null, LlmCallCause.OverLength, request.SponsorName, startedAt, mode, systemPrompt, userPrompt,
                    cfg.Model, raw));
            }

            var hygienic = ApplyLineAwareHygiene(raw);
            if (hygienic.Length == 0)
            {
                return new AdRulesReply(raw, null, Failed(
                    "the completion was empty after cleanup", ruleId: null, LlmCallCause.EmptyCompletion,
                    request.SponsorName, startedAt, mode, systemPrompt, userPrompt, cfg.Model, raw));
            }

            return new AdRulesReply(raw, hygienic, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var (outcome, cause, detail) = LlmCopyWriter.ClassifyForRing(ex);

            recorder.Record(
                request.SponsorName, systemPrompt, userPrompt, response: null, startedAt, ElapsedMs(startedAt),
                outcome, detail, mode, cause, cfg.Model, LlmCallKind.AdScript);
            logger.LogInformation(
                "Ad script failed (brand: {Brand}): {Detail}",
                request.SponsorName.ReplaceLineEndings(" "), detail.ReplaceLineEndings(" "));
            return new AdRulesReply(raw, null, new AdScriptWriteResult.Failed(detail, RuleId: null, cause));
        }
    }
}
