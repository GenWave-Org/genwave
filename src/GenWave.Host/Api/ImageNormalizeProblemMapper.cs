using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using GenWave.Host.Images;

namespace GenWave.Host.Api;

/// <summary>
/// The shared, honest, per-<see cref="ImageNormalizeFailureReason"/> <see cref="ProblemDetails"/>
/// mapping for every controller that runs bytes through <see cref="ImageNormalizeService"/> (SPEC
/// F128.6, PLAN T291/T295/T307 rider) — EXTRACTED from <c>PersonaAvatarController</c> at T307's own
/// second-copy moment: <see cref="StationImageController"/> needed the identical mapping verbatim,
/// which is exactly the <see cref="BoundedImportBodyReader"/>/<see cref="CatalogInstallShell"/>
/// lesson this codebase already learned twice — a second copy of a security/correctness-bearing
/// mapping is never grown independently, it is extracted to one shared home the moment it would
/// otherwise be pasted.
/// <para>
/// <see cref="ImageNormalizeFailureReason.EncodeFailed"/> covers several distinct underlying causes
/// (a missing/unusable ffmpeg binary, a genuinely corrupt input ffmpeg's own decoder refuses) — none
/// of which is a "decode" problem specifically, so its own title stays deliberately generic ("could
/// not be processed") rather than naming a stage this reason does not uniquely pin down; F15.7
/// already forbids naming the exact gate/gate-internal detail in any of these bodies regardless.
/// <see cref="ImageNormalizeFailureReason.OutputTooLarge"/> (gh-#520) is DELIBERATELY split out of
/// that generic bucket rather than folded into it — a successfully re-encoded image that is merely
/// too big to store is a genuinely different, honestly-nameable claim ("too large"), never the
/// misleading "could not be processed" copy the pre-#520 EncodeFailed catch-all used to hand back for
/// this exact case (the root cause of gh-#520's own bug report).
/// </para>
/// </summary>
static class ImageNormalizeProblemMapper
{
    /// <summary>Maps <paramref name="reason"/> to its own honest, distinct-titled 400
    /// <see cref="ProblemDetails"/> — see this type's own remarks for why no two reasons ever share a
    /// title, and why <see cref="ImageNormalizeFailureReason.EncodeFailed"/>'s own title stays
    /// deliberately generic.</summary>
    public static ProblemDetails ToProblem(ImageNormalizeFailureReason reason) => reason switch
    {
        ImageNormalizeFailureReason.Empty => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "Empty upload.",
            Detail = "The request body was empty.",
        },
        ImageNormalizeFailureReason.TooLarge => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "Upload too large.",
            Detail = $"The uploaded image must be at most {ImageNormalizeService.MaxInputBytes / (1024 * 1024)} MiB.",
        },
        ImageNormalizeFailureReason.NotAnImage => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "Unsupported image format.",
            Detail = "The uploaded file is not a recognized PNG or JPEG image.",
        },
        ImageNormalizeFailureReason.Animated => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "Animated images are not supported.",
            Detail = "An animated PNG (APNG) cannot be used here.",
        },
        ImageNormalizeFailureReason.DimensionsTooSmall => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "Image too small.",
            Detail = $"The uploaded image must be at least {ImageNormalizeService.MinDimensionPx}x{ImageNormalizeService.MinDimensionPx} pixels.",
        },
        ImageNormalizeFailureReason.DimensionsTooLarge => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "Image dimensions too large.",
            Detail = $"The uploaded image must be at most {ImageNormalizeService.MaxDimensionPx}x{ImageNormalizeService.MaxDimensionPx} pixels.",
        },
        ImageNormalizeFailureReason.EncodeFailed => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "Could not process image.",
            Detail = "The uploaded image could not be processed.",
        },
        ImageNormalizeFailureReason.OutputTooLarge => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title  = "Processed image too large.",
            Detail = "The processed image is too large to store.",
        },
        _ => throw new UnreachableException($"Unhandled {nameof(ImageNormalizeFailureReason)} case."),
    };
}
