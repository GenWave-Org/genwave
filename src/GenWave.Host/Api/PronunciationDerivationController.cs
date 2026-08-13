using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Host.Pronunciations;

namespace GenWave.Host.Api;

/// <summary>
/// POST /api/pronunciations/derive — owner-only respell→IPA assist for the pronunciation rules
/// editor (SPEC F126.2, STORY-324, PLAN T278). The operator types a respelling ("muh-KLOWD"), this
/// endpoint runs it through the vendored espeak-ng binary (<see cref="IRespellOracle"/>) and hands
/// back candidate IPA RAW, for the operator to audition (<c>POST /api/tts/preview</c>,
/// <see cref="TtsPreviewController"/>, STORY-323) and adjust before ever saving it as a
/// <see cref="PronunciationsController"/> row. T279's editor half wires a "Derive" button to this
/// call and, ORIGINALLY, hid it only the first time it 501s (attempt-and-hide) — gh-#487 replaced
/// that with <see cref="Available"/> below, a pre-flight probe the editor calls once on mount, so an
/// espeak-less image hides the assist BEFORE the operator's first click rather than after one dead-
/// end 501. The 501 path here still exists as the fallback for whatever that probe itself cannot
/// catch (see <see cref="Available"/>'s own remarks).
///
/// <para>
/// <b>A SEPARATE controller from <see cref="PronunciationsController"/></b>, sharing its
/// "api/pronunciations" route prefix the same way <see cref="TtsCorrectionsController"/> shares
/// "api/tts" with <see cref="TtsPreviewController"/> — CRUD-on-saved-rules and
/// derive-a-candidate-from-a-binary are different concerns with different failure modes (only this
/// one can 501), and splitting them keeps <see cref="PronunciationsController"/>'s own dependency
/// list from growing a process adapter it would otherwise never need.
/// </para>
///
/// <para>
/// <b>501 when the binary is absent</b> (T278): checked via <see cref="IRespellOracle.IsAvailable"/>
/// — which latches false the first time starting espeak-ng fails with "not found" and never
/// re-probes — BEFORE ever calling <see cref="IRespellOracle.DeriveAsync"/>, so an image built
/// without the package degrades to a cheap, permanent 501 for the rest of this process's life. Raw-
/// IPA authoring and the STORY-323 audition loop stand alone without this assist.
/// </para>
///
/// <para>
/// <b>The capability probe (gh-#487):</b> <see cref="Available"/> reads the exact same
/// <see cref="IRespellOracle.IsAvailable"/> latch this 501 check reads — no process spawn, cheap
/// enough for the editor to call once per mount — so an operator on an espeak-less image never has
/// to spend a dead-end click discovering that before ever seeing the assist at all. A probe result
/// is a snapshot, not a subscription: the latch can only ever flip true→false within one process
/// lifetime (never back), so a probe taken before that flip and a POST attempted after it would
/// still correctly land here, in the very code path this paragraph documents.
/// </para>
///
/// <para>
/// <b>Input is capped and inert</b> (T278): <see cref="RespellDeriveRequest.Respelling"/> is capped
/// at <see cref="MaxRespellingLength"/> characters (400 beyond) and rejects raw control characters —
/// widened to <c>U+2028</c>/<c>U+2029</c> too (line/paragraph separator — <see cref="char.IsControl"/>
/// does not cover either; review round 2 observation) — a respelling is a word or short phrase an
/// operator typed, never carrying a stray CR/LF. Beyond the cap, a shell metacharacter like
/// <c>$(rm -rf /)</c> is not a special case this endpoint defends against by filtering it:
/// <see cref="EspeakRespellOracle"/> passes it to the underlying process via
/// <c>ProcessStartInfo.ArgumentList</c> — never a composed shell string — so it always arrives as one
/// inert literal argument, structurally, not by any check here.
/// </para>
///
/// <para>
/// <b>Leading <c>-</c> is refused here too (review round 2 finding F1, defence-in-depth).</b> The
/// ACTUAL fix for the argument-injection class (CWE-88: espeak-ng parsing a respelling shaped like
/// <c>-f/root/appsettings.json</c> or <c>--phonout=/path</c> as another OPTION rather than data —
/// see <see cref="EspeakRespellOracle"/>'s own remarks for the proven-in-container exploit and its
/// fix) lives structurally in <see cref="EspeakRespellOracle.BuildProcessStartInfo"/>, which inserts
/// the POSIX <c>--</c> end-of-options marker before the respelling — that alone makes every
/// leading-<c>-</c> shape inert. This endpoint refuses one anyway, before the oracle is ever called:
/// belt and braces, so a future change to the adapter that ever dropped the <c>--</c> marker would
/// still not be reachable from THIS surface with an option-shaped payload.
/// </para>
///
/// <para>
/// <b>Never persisted, never logged</b> (SPEC F126.5's "log the event, not the text" — mirrors
/// <see cref="TtsPreviewController"/>'s own audition-event line): the derived IPA is returned raw
/// for the operator to audition and this endpoint never writes it anywhere; the respelling itself
/// is likewise never logged. One Information line per accepted request names only the input length.
/// </para>
/// </summary>
[ApiController]
[Route("api/pronunciations")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class PronunciationDerivationController(
    IRespellOracle oracle,
    ILogger<PronunciationDerivationController> logger) : ControllerBase
{
    /// <summary>
    /// Attacker/mistake-controlled input length cap (T278). A respelling is a word or short phrase,
    /// never a paragraph: 200 characters is generous headroom over any real one, and it turns a
    /// pasted-in essay into a fast 400 instead of a multi-second espeak-ng render.
    /// </summary>
    const int MaxRespellingLength = 200;

    [HttpPost("derive")]
    [Consumes("application/json")]
    public async Task<IActionResult> Derive([FromBody] RespellDeriveRequest request, CancellationToken ct)
    {
        var respelling = request.Respelling ?? "";

        if (string.IsNullOrWhiteSpace(respelling))
            return BadRequest(InvalidRespellingProblem("respelling must not be blank or whitespace."));

        if (respelling.Length > MaxRespellingLength)
            return BadRequest(InvalidRespellingProblem($"respelling must not exceed {MaxRespellingLength} characters."));

        if (respelling.Any(IsRejectedControlCharacter))
            return BadRequest(InvalidRespellingProblem("respelling must not contain control characters."));

        if (respelling[0] == '-')
        {
            return BadRequest(InvalidRespellingProblem(
                "respelling must not start with '-' — reserved for espeak-ng options."));
        }

        if (!oracle.IsAvailable)
            return EspeakUnavailable();

        // The EVENT, not the text (SPEC F126.5) — length only, never the respelling or the IPA it
        // derives. Mirrors TtsPreviewController's own audition-event line.
        logger.LogInformation("Respell derive requested: length={Length}", respelling.Length);

        var ipa = await oracle.DeriveAsync(respelling, ct);
        if (ipa is null)
        {
            // The oracle can discover absence mid-call (a race against the IsAvailable pre-check
            // above) as well as fail for an unrelated reason (bad exit, timeout) — re-checking here
            // reports whichever one actually happened rather than always assuming the latter.
            return oracle.IsAvailable ? DeriveFailed() : EspeakUnavailable();
        }

        return Ok(new RespellDeriveResponse(ipa));
    }

    /// <summary>
    /// GET /api/pronunciations/derive/available (gh-#487) — a cheap pre-flight capability probe the
    /// editor calls once on mount, before the operator's first click, so an espeak-less image can
    /// hide the "Derive" assist up front instead of learning it only after one dead-end 501
    /// (T279's original attempt-and-hide). Reads the SAME latched
    /// <see cref="IRespellOracle.IsAvailable"/> the <see cref="Derive"/> action above pre-checks —
    /// no process is ever spawned to answer this, so it costs nothing to call on every mount of the
    /// pronunciation rules editor.
    /// </summary>
    [HttpGet("derive/available")]
    public IActionResult Available() => Ok(new RespellAvailabilityResponse(oracle.IsAvailable));

    // Not named "ValidationProblem": ControllerBase already declares an instance method by that
    // exact name (review round 2 finding F7) — a same-named static helper here compiles but hides
    // it, which is exactly the confusion the sibling controllers' own naming convention
    // (InvalidRuleProblem, DuplicateRuleProblem, NotFoundProblem in PronunciationsController) avoids
    // by naming each helper after WHAT it represents rather than a generic, collision-prone verb.
    static ProblemDetails InvalidRespellingProblem(string detail) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = detail,
    };

    // U+2028/U+2029 (LINE/PARAGRAPH SEPARATOR) are Unicode category Zl/Zp, not Cc — char.IsControl
    // does not cover either, yet some renderers/log viewers treat them like a newline (review round
    // 2 observation). A respelling is a word or short phrase; neither belongs in one.
    static bool IsRejectedControlCharacter(char c) => char.IsControl(c) || c is '\u2028' or '\u2029';

    ObjectResult EspeakUnavailable() =>
        StatusCode(StatusCodes.Status501NotImplemented, new ProblemDetails
        {
            Status = StatusCodes.Status501NotImplemented,
            Title  = "The respell assist is unavailable.",
            Detail = "espeak-ng is not present in this image; author IPA directly instead.",
        });

    ObjectResult DeriveFailed() =>
        StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
        {
            Status = StatusCodes.Status502BadGateway,
            Title  = "Respell derivation failed.",
            Detail = "Candidate IPA could not be derived. Check the server logs for details.",
        });
}
