namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F127.8 (STORY-328, PLAN T285) — the thin accessor seam between
/// <c>GenWave.Orchestration.CrosstalkPlanner</c> (which cannot see the Host's
/// <c>IOptionsMonitor&lt;GenWave.Tts.CrosstalkOptions&gt;</c> directly) and the Host's live
/// <c>Crosstalk:Shows</c>/<c>Crosstalk:EveryNthAiring</c> values. Mirrors
/// <see cref="IShowPatterCadenceProvider"/> exactly — same "Orchestration cannot reference Host
/// options, so a narrow contract lives here instead" shape, one seam over.
///
/// <para>
/// <b><see cref="EnabledShows"/> empty means the feature is OFF (SPEC F127.8)</b> — no station's
/// sound changes on upgrade until an operator explicitly names a show. Implementations MUST
/// re-evaluate both members fresh on every read — never cache them in a field — the same
/// discipline <see cref="IShowPatterCadenceProvider.PatterCadenceMinutes"/> follows, so a live
/// <c>PUT /api/settings</c> edit reaches the very next casting/eligibility check with no api
/// restart.
/// </para>
///
/// <para>
/// <b><see cref="EnabledShows"/> holds SLUGS, not display names (PLAN T285 review F4)</b> — a show's
/// display name is mutable and non-unique; keying eligibility on it would let an operator's rename
/// silently kill banter forever. <c>station.show.slug</c> (db/35) is the stable identity, the same
/// "names slugs, not labels" rule <c>GenWave.Host.Configuration.SettingValidator</c>'s own
/// <c>Station:Theme</c> guard already follows (T175).
/// </para>
/// </summary>
public interface ICrosstalkScopeProvider
{
    /// <summary>The live set of show SLUGS crosstalk may air for (SPEC F127.8, PLAN T285 review F4)
    /// — matched case-insensitively against <c>GenWave.Core.Domain.ShowSummary.Slug</c>. Empty is the
    /// fail-closed default: the feature is off everywhere until an operator names at least one
    /// show. Evaluated fresh on every call.</summary>
    IReadOnlyList<string> EnabledShows { get; }

    /// <summary>How many eligible airings of an enabled show pass before one carries banter (SPEC
    /// F127.8) — 1 (the default) airs every time; the counting itself is a LATER task's own concern
    /// (PLAN T287's vend gate). Evaluated fresh on every call.</summary>
    int EveryNthAiring { get; }
}
