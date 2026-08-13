using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Logging;
using GenWave.Host.Options;
using GenWave.Tts;
using TtsRenderContext = GenWave.Core.Domain.TtsRenderContext;

namespace GenWave.Host.Api;

/// <summary>
/// POST /api/tts/preview — synchronous <c>audio/wav</c> preview of arbitrary text (SPEC F35.6,
/// F126.1; STORY-123, STORY-323). Calls <see cref="ITtsSynthesizer"/> — the same production Kokoro
/// client patter uses — directly, with no <c>TtsSegmentSource</c> in front of it: no loudness/cue
/// measurement, no <c>MediaItem</c>, no catalog row, no engine annotation. Bounded by
/// <c>Tts:RenderBudgetSeconds</c>, mirroring <c>SafeSegmentsController</c>'s render-budget/502 shape.
///
/// <para>
/// <b>The audition renders through the resolved rules</b> (SPEC F126.1, PLAN T274; amending
/// STORY-123's original "no seam" shape): every request now renders through
/// <see cref="ITtsSynthesizer"/>'s context-aware <c>SynthesizeAsync</c> overload,
/// carrying the SAME station∪persona pronunciation-rule merge <c>TtsSegmentSource</c> resolves for a
/// real on-air render (SPEC F97.3, F97.4) — an audition that ignored an operator's own saved rules
/// would make the rules editor lie about the thing it auditions. <see cref="TtsPreviewRequest.CandidateRules"/>
/// layers an UNSAVED rule over that merge for this render only (STORY-323 AC2), so the editor's
/// "Hear it" button proves the exact rule being authored before it is ever saved. <b>Pace ruling
/// (T274):</b> the audition should sound like air — now that rules ride the context, the active
/// persona's resolved pace rides alongside them too, rather than the engine default T140's own
/// review noted every preview rendered at.
/// </para>
///
/// <para>
/// <b>Rule hits never count from an audition</b> (SPEC F97.5, F126.1; STORY-253 AC6): the context
/// built here always carries <c>IsAudition = true</c> — see
/// <see cref="GenWave.Tts.PronunciationRuleHitReporter"/>'s own remarks for the full mechanism and
/// the F126.5 ruling on what "auditions log at Information" actually names. <b>The AUDITION event
/// itself</b> (SPEC F126.5, T274 review finding F2) — as opposed to a per-rule hit, which never
/// fires here — logs its own single Information line per accepted request, naming the voice and
/// candidate count only: never rule text (Pattern/Word/Ipa) or the operator-authored preview text
/// itself, both of which stay unlogged.
/// </para>
///
/// PERSISTENCE (SPEC F35.6 "not persisted"): <see cref="ITtsSynthesizer.SynthesizeAsync"/>
/// writes its result to a transient, Guid-named file under <c>Tts:CacheRoot</c> as an unavoidable
/// side effect of synthesizing (<c>GenWave.Tts.TransientRenderPath</c> — never content-addressed,
/// SPEC F98.2 as amended: there is no "engine file cache", only scratch space one write wide) — that
/// write is the synthesizer's own production contract (the same file <c>TtsSegmentSource</c> would
/// move into the station's forever-cache on a real render), not something a caller can suppress
/// without a second synth overload. This endpoint reads the bytes into memory for the response and
/// then deletes the file it just caused to be written (best-effort — a failed delete only leaves an
/// orphan; the path is unique to THIS call, never a hash any other render could ever compute or
/// collide with, so an orphan here is inert — no selection path ever looks for it). Nothing is
/// measured, cued, wrapped in a <c>MediaItem</c>, or written to <c>library.media</c> — there is no
/// reachable path from this call into rotation. <see cref="TtsPreviewRequest.CandidateRules"/>
/// itself is likewise never persisted — it lives only for the duration of this one render.
/// </summary>
[ApiController]
[Route("api/tts")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Operator)]
public sealed class TtsPreviewController(
    ITtsSynthesizer synthesizer,
    IOptionsMonitor<StationOptions> stationMonitor,
    IOptionsMonitor<TtsOptions> ttsMonitor,
    PronunciationRuleProvider pronunciations,
    ActivePersonaPronunciationRulesCache personaPronunciations,
    ActivePersonaPaceCache personaPace,
    ILogger<TtsPreviewController> logger) : ControllerBase
{
    // The AUDITION event log line's fixed "kind" discriminator (SPEC F126.5, T274 review finding
    // F2) — see the Preview method's own remarks for why this is a constant, not a SegmentKind.
    const string AuditionEventKind = "preview";

    // Attacker/mistake-controlled array length: System.Text.Json enforces no upper bound of its
    // own, and each candidate below pays for a PronunciationRuleValidator.Validate call (a handful
    // of string operations) plus, once accepted, a PronunciationRuleSet.Create compile (a regex
    // build) — bounded here so a huge pasted candidateRules payload costs a fast 400, not a slow
    // request, on this owner-only but still browser-reachable surface (T274 review finding F5).
    const int MaxCandidateRules = 20;

    /// <summary>See the class remarks for the full contract.</summary>
    [HttpPost("preview")]
    [Consumes("application/json")]
    public async Task<IActionResult> Preview([FromBody] TtsPreviewRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Validation error.",
                Detail = "text must not be blank or whitespace.",
            });
        }

        // Candidate-rule layering (SPEC F126.1, STORY-323 AC2/AC4): validated BEFORE any rule
        // resolution or render — a malformed candidate 400s naming the offending field, and no
        // render runs on rejection (there is nothing to persist here either way).
        var (candidateProblem, candidates) = ValidateCandidates(request.CandidateRules);
        if (candidateProblem is not null)
            return BadRequest(candidateProblem);

        var voice  = string.IsNullOrWhiteSpace(request.Voice) ? stationMonitor.CurrentValue.Voice : request.Voice;
        var budget = TimeSpan.FromSeconds(ttsMonitor.CurrentValue.RenderBudgetSeconds);

        // The AUDITION event itself (SPEC F126.5, T274 review finding F2) — one Information line
        // per accepted request, logged unconditionally here (before the render can fail), naming
        // the event kind (a fixed discriminator, so this line joins the render-observability
        // family's shared "kind=" filter/aggregate convention — TtsSegmentSource.LogRenderOutcome,
        // PronunciationRuleHitReporter.Report — rather than a SegmentKind this endpoint never has),
        // the voice, and the candidate count. Never rule text or the operator's preview text: this
        // is the "a request happened" fact, not a hit — PronunciationRuleHitReporter owns that one,
        // and stays silent for every render this endpoint produces (IsAudition, below). voice is
        // wire-controlled (request.Voice, or Station:Voice) — LogSanitize.Strip before it reaches
        // the line (CodeQL cs/log-forging, T274 round-2 review finding R1: a raw CRLF in Voice
        // forged a second log entry, reproduced), the same idiom every other rule-hit/correction
        // line in this family already applies to its own operator-authored fields.
        logger.LogInformation(
            "TTS audition requested: kind={Kind} voice={Voice} candidateCount={CandidateCount}",
            AuditionEventKind, LogSanitize.Strip(voice), candidates.Count);

        using var boundedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        boundedCts.CancelAfter(budget);

        string path;
        try
        {
            var context = await BuildAuditionContextAsync(request.Text, voice, candidates, boundedCts.Token);
            path = await synthesizer.SynthesizeAsync(context, boundedCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // boundedCts fired on the render budget, not the caller disconnecting.
            logger.LogWarning(
                "TTS preview synthesis exceeded Tts:RenderBudgetSeconds={BudgetSeconds}s", budget.TotalSeconds);
            return BadGateway();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TTS preview synthesis failed");
            return BadGateway();
        }

        byte[] bytes;
        try
        {
            bytes = await System.IO.File.ReadAllBytesAsync(path, ct);
        }
        finally
        {
            DeletePreviewArtifact(path);
        }

        return File(bytes, "audio/wav");
    }

    /// <summary>
    /// Resolves the SAME station∪persona∪candidate rule set <see cref="PronunciationRuleResolver.ResolveForRender"/>
    /// resolves for <c>TtsSegmentSource</c>'s real on-air render (SPEC F97.3, F97.4, F126.1; T274
    /// review finding F3) — read fresh for this one request, never a stale ambient snapshot — via
    /// the SAME shared seam, so audition/air parity is a property of one implementation, never a
    /// coincidence two call sites happen to agree on today. <paramref name="candidates"/> is already
    /// validated by the caller, so <see cref="PronunciationRuleSet.Create"/> compiles every entry
    /// cleanly.
    ///
    /// Pace (SPEC F98.1-F98.3; PLAN T274 ruling — see the class remarks): resolved the same
    /// "one ambient-persona read, right before use" way <c>TtsSegmentSource</c> resolves it, so an
    /// audition renders at the SAME rate the persona would actually air at, not the engine default.
    ///
    /// <see cref="TtsRenderContext.IsAudition"/> is set unconditionally — see
    /// <see cref="GenWave.Tts.PronunciationRuleHitReporter"/>'s own remarks for the exclusion this
    /// drives, and <see cref="TtsRenderContext.IsAudition"/>'s remarks for the T276 sibling ruling.
    /// </summary>
    async Task<TtsRenderContext> BuildAuditionContextAsync(
        string text, string voice, IReadOnlyList<PronunciationRule> candidates, CancellationToken ct)
    {
        await personaPronunciations.RefreshIfStaleAsync(ct);
        var contextRules = PronunciationRuleResolver.ResolveForRender(
            pronunciations.Current, personaPronunciations.Current, candidates);

        await personaPace.RefreshIfStaleAsync(ct);

        return new TtsRenderContext(text, voice, Kind: null)
        {
            Rules      = contextRules,
            Pace       = personaPace.Current,
            IsAudition = true,
        };
    }

    /// <summary>
    /// Validates every <see cref="TtsPreviewRequest.CandidateRules"/> entry via
    /// <see cref="PronunciationRuleValidator"/> — the SAME filter <see cref="PronunciationRuleSet.Create"/>
    /// itself applies at compile time (SPEC F97.5's declared-vs-compiled honesty, extended to this
    /// write-adjacent surface, mirroring <c>PronunciationsController</c>'s write-path guard, and
    /// sharing its <see cref="PronunciationRuleProblemDetails"/> field-naming helper, T274 review
    /// finding F4). Every failing entry contributes to ONE <see cref="ValidationProblemDetails"/>
    /// (STORY-323 AC4: "400s naming the field") rather than stopping at the first, so an operator
    /// authoring several candidates in one preview sees every offending field at once. Field keys
    /// are index-qualified (<c>candidateRules[0].pattern</c>) so a caller can tell which candidate
    /// failed which check.
    ///
    /// <paramref name="candidates"/> is System.Text.Json-deserialized, untrusted input:
    /// <c>[null]</c> is a literal, structurally valid JSON array System.Text.Json happily binds
    /// regardless of <see cref="TtsPreviewCandidateRule"/>'s own non-nullable element type (nullable
    /// reference types are a compile-time-only annotation, never enforced by the deserializer) — a
    /// null element 400s naming its own index (<c>candidateRules[0]</c>, no trailing field) rather
    /// than dereferencing it (T274 review finding F1). The array itself is length-capped at
    /// <see cref="MaxCandidateRules"/> before any per-element work runs (T274 review finding F5).
    ///
    /// Returns the resolved <see cref="PronunciationRule"/> list on success — empty for a null/empty
    /// request, never null itself, so the caller never needs its own null-vs-empty branch.
    /// </summary>
    static (ValidationProblemDetails? Problem, IReadOnlyList<PronunciationRule> Rules) ValidateCandidates(
        IReadOnlyList<TtsPreviewCandidateRule>? candidates)
    {
        if (candidates is null || candidates.Count == 0)
            return (null, []);

        var problem = new ValidationProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "One or more candidate pronunciation rule fields are invalid.",
        };

        if (candidates.Count > MaxCandidateRules)
        {
            problem.Errors["candidateRules"] =
                [$"candidateRules must not exceed {MaxCandidateRules} entries."];
            return (problem, []);
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate is null)
            {
                problem.Errors[$"candidateRules[{i}]"] = ["Candidate rule must not be null."];
                continue;
            }

            var errors = PronunciationRuleValidator.Validate(candidate.Pattern ?? "", candidate.Word, candidate.Ipa ?? "");
            PronunciationRuleProblemDetails.AddErrors(problem, errors, keyPrefix: $"candidateRules[{i}]");
        }

        if (problem.Errors.Count > 0)
            return (problem, []);

        var resolved = candidates
            .Select(c => new PronunciationRule(c.Pattern ?? "", c.Word ?? "", c.Ipa ?? ""))
            .ToList();
        return (null, resolved);
    }

    /// <summary>
    /// Best-effort cleanup of the file <see cref="ITtsSynthesizer.SynthesizeAsync"/> just wrote — see
    /// the class remarks for why this is the smallest honest way to keep a preview from
    /// accumulating an artifact that looks like content.
    /// </summary>
    void DeletePreviewArtifact(string path)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not delete TTS preview artifact {Path}; it will be orphaned in the cache", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Could not delete TTS preview artifact {Path}; it will be orphaned in the cache", path);
        }
    }

    ObjectResult BadGateway() =>
        StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
        {
            Status = StatusCodes.Status502BadGateway,
            Title  = "TTS preview generation failed.",
            Detail = "The preview audio could not be generated. Check the server logs for details.",
        });
}
