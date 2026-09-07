namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of an
/// <see cref="Abstractions.IJinglePackStore.DeleteAsync"/> call (SPEC F165.6, STORY-401, PLAN T414) —
/// mirrors <see cref="VoicePackDeleteResult"/>'s own closed-hierarchy shape (a private base
/// constructor, sealed record cases) for the same exhaustive-switch guarantee. See
/// <see cref="JinglePackUpsertResult"/>'s own "Honest boundary" remarks for why this uninstall, like
/// that install, is a guard-read-then-write across TWO connections (<c>station_svc</c> +
/// <c>library_svc</c>) rather than <see cref="VoicePackDeleteResult"/>'s own single in-statement
/// guard — the same db/22 schema-role boundary, cited there from db/42's and db/45's own header
/// remarks, applies unchanged to a delete.
/// </summary>
public abstract record JinglePackDeleteResult
{
    private JinglePackDeleteResult() { }

    /// <summary>The pack row and every one of its <c>library.media</c> asset rows were removed.
    /// <see cref="Paths"/> names every asset's own engine-visible path, read BEFORE either delete ran
    /// — the controller unlinks each one from disk once this result is returned, then removes the
    /// pack's own slug folder under <c>Packs:JingleRoot</c> if it is left empty.</summary>
    public sealed record Deleted(IReadOnlyList<string> Paths) : JinglePackDeleteResult;

    /// <summary>No installed pack with the requested slug exists.</summary>
    public sealed record NotFound : JinglePackDeleteResult;

    /// <summary>
    /// The delete was refused because at least one of this pack's asset rows still sits behind an
    /// active <c>station.ad_spot.bed_media_id</c> reference (an ad spot in <c>draft</c>/
    /// <c>approved</c>/<c>rendering</c>/<c>ready</c> — the same active-state set
    /// <see cref="VoicePackDeleteResult.Referenced"/>'s own voice-plan guard uses). Nothing was
    /// removed on either side of the schema boundary. <see cref="AdSpotIds"/> names every offending
    /// spot, re-queried purely to report them, the same "may rarely be empty if the race closed the
    /// other way" caveat <see cref="VoicePackDeleteResult.Referenced"/> already carries.
    /// </summary>
    public sealed record Refused(IReadOnlyList<long> AdSpotIds) : JinglePackDeleteResult;
}
