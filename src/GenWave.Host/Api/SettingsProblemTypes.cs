namespace GenWave.Host.Api;

/// <summary>
/// RFC 7807 <c>type</c> URIs this station's settings endpoints stamp on a <c>ProblemDetails</c>
/// body, so a caller can tell two different 409 causes apart without pattern-matching the human
/// <c>detail</c> text (gh-#486). Shared by <see cref="SettingsController"/> (the batch
/// <c>PUT /api/settings</c> path) and <see cref="PronunciationsController"/> (its own dedicated
/// <c>/api/pronunciations</c> CRUD, which writes the same <c>Tts:Pronunciations</c> key) so the two
/// surfaces can never drift onto different URIs for the identical cause.
/// </summary>
public static class SettingsProblemTypes
{
    /// <summary>
    /// A version-guarded write's <c>expectedVersion</c> no longer matches the stored value — the row
    /// moved under this write (another editor saved first). The admin UI's response: refetch and
    /// tell the operator their view was stale, never a silent merge.
    /// </summary>
    public const string VersionConflict = "https://genwave.radio/problems/settings-version-conflict";
}
