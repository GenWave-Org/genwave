using System.Text.Json;
using GenWave.Abstractions.Playout;
using GenWave.Core.Logging;
using Microsoft.Extensions.Logging;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The one house (de)serialization for <c>station.show.envelope</c>'s <c>rotation</c> key (SPEC
/// F152.3, F152.4, STORY-372, PLAN T360) — shared by <see cref="ShowRepository"/> (the
/// <c>station.show</c> CRUD entity) and <see cref="ScheduleRepository"/>/<see cref="SpecialsRepository"/>
/// (the resolver-snapshot <c>ShowSummary</c> projection), so every reader of this one jsonb key
/// normalizes identically. Every OTHER <c>envelope</c> key and <c>persona_id</c> stay entirely unread
/// by every caller of this type — SPEC F115.2's dormant-columns-unread pin, unchanged by this file.
/// </summary>
static class RotationEnvelopeCodec
{
    static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Parses <paramref name="rotationJson"/> — the raw text a query's own <c>envelope ->> 'rotation'</c>
    /// extracted — into a <see cref="RotationPredicate"/>. Postgres's <c>-&gt;&gt;</c> operator already
    /// collapses a missing <c>envelope</c>, an absent <c>rotation</c> key, and an explicit JSON
    /// <c>null</c> value down to a single SQL NULL, so <paramref name="rotationJson"/> is
    /// <see langword="null"/> for all three and this method returns <see langword="null"/> straight
    /// back. An empty object (<c>{}</c>) or both members explicitly <c>null</c> also normalize to
    /// <see langword="null"/> (T356's own review note: the relax ladder must never stamp
    /// <c>RotationRelax = 0</c> for a predicate that filters nothing). A malformed value — not a JSON
    /// object, or a member that is not an integer — normalizes to <see langword="null"/> too, plus one
    /// WARN naming <paramref name="showName"/>: a corrupted row must never throw mid-airing (F152.4's
    /// never-silence posture). Unknown extra keys inside the object are silently ignored
    /// (<see cref="System.Text.Json"/>'s own default unmapped-member handling).
    ///
    /// <para>
    /// PLAN T360 review MED-2 (CodeQL <c>cs/log-forging</c>, CWE-117): <paramref name="showName"/> is
    /// operator-authored free text — <c>ShowRepository.ValidateName</c> only rejects blank/fallback-slug
    /// names, never CR/LF — so it runs through <see cref="LogSanitize.Strip(string?)"/> before it ever
    /// reaches this WARN, the same gate <c>FontPackRepository.ResolveFileCollisionAsync</c> and
    /// <c>ShowsController.Create</c> already apply to this identical field. The raw
    /// <paramref name="rotationJson"/> value is never logged at all — malformed, attacker/operator-
    /// controlled text carries no diagnostic value proportionate to the forging surface it would open.
    /// </para>
    /// </summary>
    public static RotationPredicate? Parse(string? rotationJson, string showName, ILogger logger)
    {
        if (rotationJson is null) return null;

        RotationPredicate? predicate;
        try
        {
            predicate = JsonSerializer.Deserialize<RotationPredicate>(rotationJson, Options);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex,
                "station.show '{ShowName}' has a malformed envelope.rotation; treating it as none",
                LogSanitize.Strip(showName));
            return null;
        }

        return predicate is null or { MaxPlays: null, NotAiredWithinDays: null } ? null : predicate;
    }

    /// <summary>The write-side mirror of <see cref="Parse"/> — the exact JSON fragment
    /// <see cref="ShowRepository.SetRotationAsync"/> merges into <c>envelope</c>'s <c>rotation</c> key.
    /// <see langword="null"/> tells the caller to remove the key instead of writing one (see that
    /// method's own remarks). A both-null <paramref name="rotation"/> (<c>MaxPlays</c> and
    /// <c>NotAiredWithinDays</c> both null) is treated identically to <see langword="null"/> itself
    /// (PLAN T360 review LOW-6): without this guard the store would happily persist
    /// <c>{"maxPlays":null,"notAiredWithinDays":null}</c> — a value <see cref="Parse"/> immediately
    /// normalizes back to <see langword="null"/> on the very next read — leaving a filters-nothing
    /// fragment sitting in the document forever instead of the key being removed outright. Keeping
    /// this guard here (not only in <see cref="Parse"/>) makes the store self-consistent: nothing this
    /// type ever WRITES needs the both-null normalization that reads must still tolerate for rows
    /// written before this guard existed, or hand-populated directly.</summary>
    public static string? ToJson(RotationPredicate? rotation) =>
        rotation is null or { MaxPlays: null, NotAiredWithinDays: null }
            ? null
            : JsonSerializer.Serialize(rotation, Options);
}
