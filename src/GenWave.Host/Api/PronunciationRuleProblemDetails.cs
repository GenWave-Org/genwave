using Microsoft.AspNetCore.Mvc;
using GenWave.Tts;

namespace GenWave.Host.Api;

/// <summary>
/// The ONE place a <see cref="PronunciationRuleValidator"/> result becomes
/// <see cref="ValidationProblemDetails"/> entries (T274 review finding F4) — shared by
/// <see cref="PronunciationsController"/>'s single-rule write path (<c>POST</c>/<c>PUT</c>
/// <c>/api/pronunciations</c>) and <see cref="TtsPreviewController"/>'s multi-candidate audition
/// path (<c>POST /api/tts/preview</c>'s <c>candidateRules</c>), so the two can never drift onto
/// different field-naming shapes for the identical underlying validator.
/// </summary>
static class PronunciationRuleProblemDetails
{
    /// <summary>
    /// Adds one <see cref="ValidationProblemDetails.Errors"/> entry per distinct
    /// <see cref="PronunciationRuleValidationError.Field"/> in <paramref name="errors"/> onto
    /// <paramref name="problem"/>, in place. <paramref name="keyPrefix"/> — when supplied —
    /// prefixes each key (e.g. <c>candidateRules[0].pattern</c>) so a caller validating several
    /// rules in one request can tell which one failed which check; <see langword="null"/> (the
    /// single-rule case) leaves the bare field name (<c>pattern</c>) PronunciationsController
    /// already shipped.
    /// </summary>
    public static void AddErrors(
        ValidationProblemDetails problem, IReadOnlyList<PronunciationRuleValidationError> errors, string? keyPrefix = null)
    {
        foreach (var group in errors.GroupBy(e => e.Field, StringComparer.OrdinalIgnoreCase))
        {
            var key = keyPrefix is null ? group.Key : $"{keyPrefix}.{group.Key}";
            problem.Errors[key] = group.Select(e => e.Message).ToArray();
        }
    }
}
