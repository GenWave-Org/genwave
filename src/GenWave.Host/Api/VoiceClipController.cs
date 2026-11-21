using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Host.Voices;

namespace GenWave.Host.Api;

/// <summary>
/// <c>POST/GET api/voices/clips</c> and <c>DELETE api/voices/clips/{slug}</c> (SPEC F219.12, F219.13, F219.15; STORY-557; PLAN T756):
/// the reference clips a cloning voice engine speaks in. The same <see cref="AdminSurfaceAttribute"/> +
/// <see cref="AuthorizationPolicies.Settings"/> pairing as <see cref="VoicePackController"/>, so an upload or delete without an admin
/// session is a 401.
///
/// <para>
/// <b>Upload.</b> A multipart form with a <c>label</c> and one <c>file</c> (WAV, FLAC or MP3, 5 to 20 s, at most 10 MiB). Anything
/// else is a 400 whose <c>detail</c> says why. The stored file name is a slug the server makes from the label, so nothing the client
/// sends reaches the disk as a path (the hostile label <c>../x.wav</c> is stored as the slug <c>x-wav</c>). See
/// <see cref="VoiceClipService"/> for the staging, conversion and cleanup rules.
/// </para>
///
/// <para>
/// <b>Delete.</b> Refused with a 409 while a DJ or a voice setting still speaks in the clip; the body names each user in plain words
/// (and in the <c>referencedBy</c> extension, as the pack uninstall 409s do).
/// </para>
/// </summary>
[ApiController]
[Route("api/voices/clips")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class VoiceClipController(VoiceClipService clips) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VoiceClipDto>>> List(CancellationToken ct) =>
        Ok((await clips.ListAsync(ct)).Select(VoiceClipDto.From).ToList());

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(VoiceClipLimits.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = VoiceClipLimits.MaxRequestBytes)]
    public async Task<IActionResult> Upload([FromForm] string? label, IFormFile? file, CancellationToken ct)
    {
        var cleanLabel = label?.Trim() ?? "";
        if (cleanLabel.Length == 0)
            return Refused("The clip needs a label.");
        if (cleanLabel.Length > VoiceClipLimits.MaxLabelLength)
            return Refused($"The label can be at most {VoiceClipLimits.MaxLabelLength} characters.");
        if (file is null || file.Length == 0)
            return Refused("Choose a WAV, FLAC or MP3 recording to upload.");
        if (file.Length > VoiceClipLimits.MaxFileBytes)
            return Refused("The file is larger than 10 MiB.");

        await using var content = file.OpenReadStream();
        return await clips.UploadAsync(cleanLabel, content, ct) switch
        {
            VoiceClipUploadResult.Created created => StatusCode(StatusCodes.Status201Created, VoiceClipDto.From(created.Clip)),
            VoiceClipUploadResult.Rejected rejected => Refused(rejected.Reason),
            var other => throw new InvalidOperationException($"Unhandled {nameof(VoiceClipUploadResult)} case {other.GetType().Name}."),
        };
    }

    [HttpDelete("{slug}")]
    public async Task<IActionResult> Delete(string slug, CancellationToken ct) =>
        await clips.DeleteAsync(slug, ct) switch
        {
            VoiceClipDeleteResult.Deleted => NoContent(),
            VoiceClipDeleteResult.NotFound => NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Voice clip not found.",
                Detail = "There is no clip with that name.",
            }),
            VoiceClipDeleteResult.InUse inUse => Conflict(InUseProblem(inUse.Users)),
            var other => throw new InvalidOperationException($"Unhandled {nameof(VoiceClipDeleteResult)} case {other.GetType().Name}."),
        };

    BadRequestObjectResult Refused(string reason) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "Voice clip refused.",
        Detail = reason,
    });

    static ProblemDetails InUseProblem(IReadOnlyList<string> users)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Voice clip is in use.",
            Detail = $"This clip is still the voice of {string.Join(", ", users)}. Choose another voice for them first.",
        };
        problem.Extensions[CatalogInstallShell.ReferencedByExtensionKey] = users.ToArray();
        return problem;
    }
}
