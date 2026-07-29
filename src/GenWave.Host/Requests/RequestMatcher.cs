using GenWave.Core.Abstractions;

namespace GenWave.Host.Requests;

/// <summary>
/// The catalog-probe decision tree (SPEC F87.5, STORY-226, PLAN T89; genre predicate gh-#131),
/// folded into the SAME background pipeline <see cref="RequestParserService"/> already runs rather
/// than a second hosted service — matching needs the just-parsed predicates
/// <see cref="RequestParserService.ParseOneAsync"/> already holds, and a separate service would need
/// its own feed/discriminator into <c>station.request</c> for zero benefit.
///
/// <para>
/// Called ONLY when the parse produced a non-empty predicate set (an empty parse is already
/// <c>unmatched</c> — <see cref="ParsedWish.IsEmpty"/> — before this class is ever reached):
/// <list type="bullet">
/// <item>artist and/or title present ⇒ <see cref="IRequestCatalogProbe.FindBestAsync"/> (a held
/// genre ANDs into that probe — predicates merge, gh-#131); a hit stamps <c>matched_media_id</c>
/// (<see cref="IRequestStore.MarkMatchedAsync"/>) — <c>status</c> stays <c>pending</c>, a match is
/// not a fulfillment (SPEC F87.6, PLAN T90);</item>
/// <item>a miss (or no artist/title at all) with a genre predicate consults
/// <see cref="IRequestCatalogProbe.HasRequestableGenreAsync"/> (gh-#131): the station stocking that
/// genre ⇒ the row stays <c>pending</c> as a genre(+mood) vibe request, resolved at pick time via
/// <see cref="IRequestCatalogProbe.FindVibeAsync"/>; the station NOT stocking it ⇒
/// <see cref="IRequestStore.MarkUnmatchedAsync"/> even when moods are also present — the predicates
/// merge as AND, so an unstockable genre can never be satisfied, and "anything metal" on a
/// metal-less station goes <c>unmatched</c> rather than being coerced into a mood pick;</item>
/// <item>a miss with no genre but a non-empty mood predicate stays <c>pending</c> as a vibe request —
/// nothing more to write, the moods are already stored by <see cref="RequestParserService"/>'s own
/// <see cref="IRequestStore.MarkParsedAsync"/> call, resolved later at pick time via the existing
/// mood-filter machinery (SPEC F86.8);</item>
/// <item>a miss with neither ⇒ <see cref="IRequestStore.MarkUnmatchedAsync"/> — nothing left to try.</item>
/// </list>
/// No predicate at all is impossible here by construction: the caller only reaches this class for a
/// non-empty parse, and a parse with no artist/title/genre has only moods left — the moods-only
/// early return below, no probe needed.
/// </para>
///
/// <para>
/// Deliberately carries no <see cref="Microsoft.Extensions.Logging.ILogger"/> at all (SPEC F87.5's
/// "silently") — every branch of this decision tree is silent by construction, not merely by
/// discipline, so no future edit can accidentally log a wish, a predicate, or even a chatty outcome
/// line for a code path the spec calls out by name as silent.
/// </para>
/// </summary>
sealed class RequestMatcher(IRequestCatalogProbe catalogProbe, IRequestStore store)
{
    public async Task MatchAsync(
        long id, string? artist, string? title, string? genre, IReadOnlyList<string> moods, CancellationToken ct)
    {
        if (artist is null && title is null && genre is null)
            return; // moods-only predicate — already a vibe request, nothing more to write.

        if (artist is not null || title is not null)
        {
            var mediaId = await catalogProbe.FindBestAsync(artist, title, genre, ct);
            if (mediaId is not null)
            {
                await store.MarkMatchedAsync(id, mediaId.Value, ct);
                return;
            }
        }

        if (genre is not null)
        {
            // gh-#131 — the genre gate: a stocked genre survives as a vibe predicate; an unstocked
            // one poisons the whole AND-merged set (moods included), so the row flips unmatched
            // rather than falling back to a mood pick the listener never asked for.
            if (await catalogProbe.HasRequestableGenreAsync(genre, ct))
                return;

            await store.MarkUnmatchedAsync(id, ct);
            return;
        }

        if (moods.Count == 0)
            await store.MarkUnmatchedAsync(id, ct);
        // else: a mood predicate survives the miss — stays pending as a vibe request (SPEC F87.5).
    }
}
