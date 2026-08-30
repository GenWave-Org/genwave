using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IShowStore"/> double (STORY-305, PLAN T240) for <c>ShowsController</c>'s
/// wire-layer specs — mirrors <c>FakeScheduleStore</c>'s own posture: this double is deliberately
/// NOT a re-implementation of <c>GenWave.MediaLibrary.Station.ShowRepository</c>'s own InvalidName/
/// budget/slug-conflict validation (that validation is proven for real, against a real Postgres
/// fixture, in <c>GenWave.MediaLibrary.Tests/Specs/Story305_ShowRepository.cs</c> — a
/// re-implementation here would be a lookalike double). The default (unscripted) behavior simply
/// echoes whatever is submitted — a fresh id, a locally-derived slug, blank/whitespace
/// tagline/flavor coerced to null (the one bit of that repository's own contract cheap enough to
/// reproduce faithfully without re-deriving any REJECTION logic) — and every write method can be
/// SCRIPTED via its own <c>Next*Result</c> property to return an exact <see cref="ShowWriteResult"/>
/// instead, for the sad-path/gate-parity facts that need one without a real repository behind them.
/// </summary>
sealed class FakeShowStore : IShowStore
{
    readonly Dictionary<long, Show> byId;
    long nextId;

    /// <inheritdoc/>
    public event Action? ShowChanged;

    /// <summary>Seeds the store with pre-existing rows (e.g. an IMPORTED show — no writer through
    /// this interface can ever produce one, mirrors how the real repository's own provenance tests
    /// insert directly rather than going through <c>ShowRepository.CreateAsync</c>).</summary>
    public FakeShowStore(IEnumerable<Show>? seed = null)
    {
        byId = (seed ?? []).ToDictionary(s => s.Id);
        nextId = byId.Count == 0 ? 1 : byId.Keys.Max() + 1;
    }

    /// <summary>Scripts the NEXT <see cref="CreateAsync"/> call's outcome verbatim, bypassing the
    /// default echo-and-store behavior. Cleared after one use.</summary>
    public ShowWriteResult? NextCreateResult { get; set; }

    /// <summary>Scripts the NEXT <see cref="UpdateAsync"/> call's outcome verbatim. Cleared after one
    /// use.</summary>
    public ShowWriteResult? NextUpdateResult { get; set; }

    /// <summary>Scripts the NEXT <see cref="DeleteAsync"/> call's outcome verbatim — the guard-path
    /// facts need <see cref="ShowWriteResult.Referenced"/> without a real
    /// <c>station.segment_schedule</c> FK to trigger it. Cleared after one use.</summary>
    public ShowWriteResult? NextDeleteResult { get; set; }

    public Task<IReadOnlyList<Show>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Show>>(byId.Values.OrderBy(s => s.Name, StringComparer.Ordinal).ToList());

    public Task<Show?> GetByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(byId.GetValueOrDefault(id));

    public Task<Show?> GetBySlugAsync(string slug, CancellationToken ct) =>
        Task.FromResult(byId.Values.FirstOrDefault(s => s.Slug == slug));

    public Task<ShowWriteResult> CreateAsync(ShowDraft draft, CancellationToken ct)
    {
        if (NextCreateResult is { } scripted)
        {
            NextCreateResult = null;
            return Task.FromResult(scripted);
        }

        var now = DateTime.UtcNow;
        var show = new Show(
            nextId++, draft.Name, Slugify(draft.Name), NullIfBlank(draft.Tagline), NullIfBlank(draft.Flavor),
            ImportedFrom: null, ImportedAt: null, now, now);
        byId[show.Id] = show;
        return Task.FromResult<ShowWriteResult>(new ShowWriteResult.Created(show));
    }

    public Task<ShowWriteResult> UpdateAsync(long id, ShowDraft draft, CancellationToken ct)
    {
        if (NextUpdateResult is { } scripted)
        {
            NextUpdateResult = null;
            return Task.FromResult(scripted);
        }

        if (!byId.TryGetValue(id, out var existing))
            return Task.FromResult<ShowWriteResult>(new ShowWriteResult.NotFound());

        var updated = existing with
        {
            Name = draft.Name,
            Slug = Slugify(draft.Name),
            Tagline = NullIfBlank(draft.Tagline),
            Flavor = NullIfBlank(draft.Flavor),
            UpdatedAt = DateTime.UtcNow,
        };
        byId[id] = updated;
        ShowChanged?.Invoke();
        return Task.FromResult<ShowWriteResult>(new ShowWriteResult.Updated(updated));
    }

    public Task<ShowWriteResult> DeleteAsync(long id, CancellationToken ct)
    {
        if (NextDeleteResult is { } scripted)
        {
            NextDeleteResult = null;
            return Task.FromResult(scripted);
        }

        return Task.FromResult<ShowWriteResult>(
            byId.Remove(id) ? new ShowWriteResult.Deleted() : new ShowWriteResult.NotFound());
    }

    /// <summary>Mirrors <c>ShowRepository.ImportAsync</c>'s own ATOMIC conditional upsert-by-slug
    /// contract (PLAN T254, F2 review finding): a fresh slug inserts a new row; an existing IMPORTED
    /// row (<c>ImportedFrom</c> non-null) keeps its id/CreatedAt and replaces every other field,
    /// re-stamping provenance unconditionally; an existing AUTHORED row (<c>ImportedFrom</c> null)
    /// declines — returns <see langword="null"/>, nothing touched — the same shape the real
    /// <c>WHERE imported_from IS NOT NULL</c> conflict clause produces. No scripting knob otherwise —
    /// the OTHER sad-path import gates (route-slug shape/reservation, budgets) all run in
    /// ShowsController.Import BEFORE this is ever reached, so there is no further store-level outcome
    /// left to script (mirrors FakeThemeStore.UpsertAsync's own unscripted shape).
    ///
    /// <para>
    /// <paramref name="rotation"/> (SPEC F152.6, PLAN T363) mirrors the real repository's own "no
    /// opinion, never a clear" rule: <see langword="null"/> leaves an existing row's own
    /// <c>Rotation</c> exactly as it stood (a fresh insert therefore starts at <c>Rotation</c>'s own
    /// <see langword="null"/> default, the identical "stays whatever it already was" shape
    /// <c>ShowRepository.CreateAsync</c>'s remarks give the real <c>envelope</c> column); non-null
    /// replaces it outright, the same "SetRotationAsync</c>-shaped write" the real merge-via-jsonb
    /// SQL performs (this in-memory double has no sibling <c>envelope</c> keys to preserve, so a plain
    /// replace is the faithful analogue).
    /// </para>
    /// </summary>
    public Task<Show?> ImportAsync(
        string slug, string name, string? tagline, string? flavor, string importedFrom,
        RotationPredicate? rotation, CancellationToken ct)
    {
        var existing = byId.Values.FirstOrDefault(s => s.Slug == slug);
        if (existing is { ImportedFrom: null })
            return Task.FromResult<Show?>(null);

        var now = DateTime.UtcNow;
        var show = existing is null
            ? new Show(
                nextId++, name, slug, NullIfBlank(tagline), NullIfBlank(flavor), importedFrom, now, now, now,
                rotation)
            : existing with
            {
                Name = name,
                Tagline = NullIfBlank(tagline),
                Flavor = NullIfBlank(flavor),
                ImportedFrom = importedFrom,
                ImportedAt = now,
                UpdatedAt = now,
                Rotation = rotation ?? existing.Rotation,
            };
        byId[show.Id] = show;
        ShowChanged?.Invoke();
        return Task.FromResult<Show?>(show);
    }

    /// <summary>Mirrors <c>ShowRepository.SetRotationAsync</c>'s own Updated/NotFound contract — no
    /// scripting knob (unlike <see cref="NextCreateResult"/>/<see cref="NextUpdateResult"/>/
    /// <see cref="NextDeleteResult"/>): no wire-layer spec needs one yet.</summary>
    public Task<ShowWriteResult> SetRotationAsync(long id, RotationPredicate? rotation, CancellationToken ct)
    {
        if (!byId.TryGetValue(id, out var existing))
            return Task.FromResult<ShowWriteResult>(new ShowWriteResult.NotFound());

        var updated = existing with { Rotation = rotation, UpdatedAt = DateTime.UtcNow };
        byId[id] = updated;
        ShowChanged?.Invoke();
        return Task.FromResult<ShowWriteResult>(new ShowWriteResult.Updated(updated));
    }

    // A deterministic, display-only stand-in for the production house Slugify (never accessible from
    // this project — internal to GenWave.MediaLibrary) — good enough for round-trip routing through
    // GetBySlugAsync-addressed routes; this double never needs to match LegacyPersonaCardMapper.Slugify
    // byte-for-byte, since that algorithm is already proven against Story305_ShowRepository.cs.
    static string Slugify(string name) =>
        string.Join('-', name.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
