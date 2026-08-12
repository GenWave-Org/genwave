using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GenWave.Core.Logging;
using GenWave.Host.Configuration;
using GenWave.Tts;

namespace GenWave.Host.Api;

/// <summary>
/// Admin-only pronunciation rules editor (SPEC F97, F100.3, STORY-254, PLAN T144): the merged
/// station∪persona view (SPEC F97.3, F97.4) an operator reads, plus create/edit/remove for the
/// STATION half. Persona/card rules are read-only here by design — a card rule is edited on the
/// card, which import already made a local copy of (F90); this surface only ever writes
/// <c>Tts:Pronunciations</c>.
///
/// <para>
/// <b>Content-addressed, not positional (T144 review findings F1/F2).</b> Station rules live as ONE
/// JSON array under the allowlisted <c>Tts:Pronunciations</c> settings key (SPEC F97.3) — there is no
/// per-rule row or id in storage. An earlier draft of this controller addressed a rule by its array
/// INDEX; that index is not stable across a save (a concurrent edit, or simply two station rules
/// sharing an identity, silently aliases two different rows onto the same index) and was reachable as
/// a 500 after a write had already committed. Every write now enforces case-insensitive
/// (<see cref="PronunciationRuleDto.Pattern"/>, <see cref="PronunciationRuleDto.Word"/>) uniqueness
/// among station rows instead, and <c>PUT</c>/<c>DELETE</c> address their target by that SAME content
/// identity, via query parameters (<c>?pattern=&amp;word=</c>) rather than a path segment — pattern
/// text can carry spaces and other characters a path segment's own escaping rules make awkward
/// (ASP.NET Core/Kestrel routing does not uniformly round-trip an encoded <c>/</c> inside a path
/// segment), while a query value round-trips arbitrary text through ordinary percent-encoding with no
/// such special-casing. A stale reference (two admin tabs, one already deleted the row) now 404s
/// rather than silently acting on whatever now occupies that position.
/// </para>
///
/// <para>
/// <b>Writes go through the SAME F19 settings machinery</b> every other operator-edited key uses
/// (<see cref="IStationSettingsStore.WriteAsync"/>) — a save reaches the very next render with no api
/// restart, exactly like a raw <c>PUT /api/settings</c> edit to this key already does.
/// </para>
///
/// <para>
/// <b>Validation.</b> <see cref="StationSettingsAllowlist"/>'s own <c>SettingValidator</c> guards
/// only the JSON SHAPE of <c>Tts:Pronunciations</c> (pattern required, word/ipa optional strings) —
/// it happily accepts a rule <see cref="PronunciationRuleSet.Create"/> would silently drop at
/// compile time (a blank ipa, an ipa carrying <c>)</c>/<c>[</c>/<c>]</c>, a word not found inside its
/// own pattern). <see cref="PronunciationRuleValidator.Validate"/> — the SAME filter
/// <see cref="PronunciationRuleSet.Create"/> itself calls — runs against every candidate BEFORE it
/// is ever written, so a rule that would never fire is refused with a 400 naming the offending
/// field (SPEC F97.5's declared-vs-compiled honesty, extended from a WARN log to the write path
/// itself), rather than silently persisted as dead weight. A rule already stored that never compiled
/// (legacy data, or a hand-edit through the raw settings API) still gets a row here — see
/// <see cref="PronunciationRuleDto.Reason"/> — rather than vanish (T144 review finding F3).
/// </para>
///
/// <para>
/// <b>Hit counts.</b> <see cref="PronunciationRuleHitStats"/> keys purely on (pattern, word) and
/// carries no source provenance (T142 review ruling) — a count is therefore only ever attached to
/// the row <see cref="PronunciationRuleDto.InEffect"/> marks as the one actually firing; a shadowed
/// or never-compiled station row always renders <see langword="null"/>, never a stale or borrowed
/// count.
/// </para>
/// </summary>
[ApiController]
[Route("api/pronunciations")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class PronunciationsController(
    IOptionsMonitor<TtsPronunciationsOptions> pronunciationOptions,
    IStationSettingsStore store,
    ActivePersonaPronunciationRulesCache activePersonaPronunciations,
    PronunciationRuleHitStats hitStats,
    ILogger<PronunciationsController> logger) : ControllerBase
{
    const string SettingKey = "Tts:Pronunciations";

    /// <summary>
    /// GET /api/pronunciations — the merged station∪persona view (SPEC F97.3, F100.3, STORY-254
    /// AC1-AC3): every rule from either source (including a station rule that never compiled, T144
    /// review finding F3), tagged with which one supplied it, whether it is the one in effect, and
    /// its hit count since process start (in-effect rows only).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        await activePersonaPronunciations.RefreshIfStaleAsync(ct);
        return Ok(BuildRows());
    }

    /// <summary>
    /// POST /api/pronunciations — appends a new station rule (SPEC F97.1, STORY-254 AC1). 201 with
    /// the created row; 400 naming the offending field for a rule
    /// <see cref="PronunciationRuleValidator"/> would refuse; 409 when a station rule with the same
    /// (Pattern, Word) identity already exists (T144 review finding F1/F2).
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Create([FromBody] PronunciationRuleWriteRequest request, CancellationToken ct)
    {
        var errors = PronunciationRuleValidator.Validate(request.Pattern ?? "", request.Word, request.Ipa ?? "");
        if (errors.Count > 0)
            return BadRequest(InvalidRuleProblem(errors));

        var rawPattern = request.Pattern ?? "";
        var rawWord = request.Word;
        var rawIpa = request.Ipa ?? "";
        var resolved = PronunciationRule.Parse(rawPattern, rawWord, rawIpa);

        var declared = ReadStationRules();
        if (FindDeclaredIndexByIdentity(declared, resolved.Pattern, resolved.Word) is not null)
            return Conflict(DuplicateRuleProblem(resolved.Pattern, resolved.Word));

        declared.Add(new PronunciationRule(rawPattern, rawWord ?? "", rawIpa));
        await WriteStationRulesAsync(declared, ct);

        logger.LogInformation(
            "Pronunciation rule created pattern={Pattern} word={Word}",
            LogSanitize.Strip(resolved.Pattern), LogSanitize.Strip(resolved.Word));

        return await CreatedOrUpdatedRowAsync(resolved, created: true, ct);
    }

    /// <summary>
    /// PUT /api/pronunciations?pattern=&amp;word= — replaces the station rule identified by the query
    /// (Pattern, Word) identity with <paramref name="request"/>'s new shape (SPEC F97.1, STORY-254
    /// AC1). 200 with the updated row; 404 when no station rule matches the query identity (including
    /// a stale reference to an already-deleted row, T144 review finding F1/F2); 409 when the NEW
    /// identity collides with a DIFFERENT existing station rule; 400 naming the offending field for a
    /// rule <see cref="PronunciationRuleValidator"/> would refuse.
    /// </summary>
    /// <remarks>
    /// <paramref name="pattern"/>/<paramref name="word"/> bind as <see langword="string"/>? — NOT
    /// <see langword="string"/> — deliberately (T144 review round 2 blocker): under
    /// <see cref="ApiControllerAttribute"/>, a non-nullable <see langword="string"/> action parameter
    /// with no default is treated as implicitly REQUIRED, so an empty/whitespace query value (the
    /// legitimate identity of a blank-pattern dead row, SPEC F3) fails automatic model validation
    /// with a 400 before this method body ever runs — the blank-identity row would be visible and
    /// labelled broken but permanently undeletable. Nullable parameters opt out of that inference;
    /// coalescing to <see cref="string.Empty"/> below reproduces the exact semantics a required
    /// non-nullable parameter would have had for every OTHER (non-blank) identity.
    /// </remarks>
    [HttpPut]
    [Consumes("application/json")]
    public async Task<IActionResult> Update(
        [FromQuery] string? pattern, [FromQuery] string? word,
        [FromBody] PronunciationRuleWriteRequest request, CancellationToken ct)
    {
        var targetPattern = pattern ?? "";
        var targetWord = word ?? "";

        var errors = PronunciationRuleValidator.Validate(request.Pattern ?? "", request.Word, request.Ipa ?? "");
        if (errors.Count > 0)
            return BadRequest(InvalidRuleProblem(errors));

        var declared = ReadStationRules();
        var targetIndex = FindDeclaredIndexByIdentity(declared, targetPattern, targetWord);
        if (targetIndex is null)
            return NotFound(NotFoundProblem(targetPattern, targetWord));

        var rawPattern = request.Pattern ?? "";
        var rawWord = request.Word;
        var rawIpa = request.Ipa ?? "";
        var resolved = PronunciationRule.Parse(rawPattern, rawWord, rawIpa);

        var collisionIndex = FindDeclaredIndexByIdentity(declared, resolved.Pattern, resolved.Word);
        if (collisionIndex is not null && collisionIndex != targetIndex)
            return Conflict(DuplicateRuleProblem(resolved.Pattern, resolved.Word));

        declared[targetIndex.Value] = new PronunciationRule(rawPattern, rawWord ?? "", rawIpa);
        await WriteStationRulesAsync(declared, ct);

        logger.LogInformation(
            "Pronunciation rule updated pattern={Pattern} word={Word} newPattern={NewPattern} newWord={NewWord}",
            LogSanitize.Strip(targetPattern), LogSanitize.Strip(targetWord),
            LogSanitize.Strip(resolved.Pattern), LogSanitize.Strip(resolved.Word));

        return await CreatedOrUpdatedRowAsync(resolved, created: false, ct);
    }

    /// <summary>
    /// DELETE /api/pronunciations?pattern=&amp;word= — removes the station rule identified by the
    /// query (Pattern, Word) identity (SPEC F97.1, STORY-254 AC1). 204 on success; 404 when no station
    /// rule matches — including a stale reference to a row another tab already deleted (T144 review
    /// finding F1/F2: never silently deletes whatever now occupies a stale position instead).
    /// </summary>
    /// <remarks>
    /// <paramref name="pattern"/>/<paramref name="word"/> bind as <see langword="string"/>? for the
    /// SAME reason <see cref="Update"/>'s own remarks give (T144 review round 2 blocker) — a
    /// non-nullable <see langword="string"/> parameter here would 400 on the blank-pattern identity
    /// before this body ever ran, making a dead row with that identity permanently undeletable.
    /// </remarks>
    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string? pattern, [FromQuery] string? word, CancellationToken ct)
    {
        var targetPattern = pattern ?? "";
        var targetWord = word ?? "";

        var declared = ReadStationRules();
        var targetIndex = FindDeclaredIndexByIdentity(declared, targetPattern, targetWord);
        if (targetIndex is null)
            return NotFound(NotFoundProblem(targetPattern, targetWord));

        var removed = declared[targetIndex.Value];
        declared.RemoveAt(targetIndex.Value);
        await WriteStationRulesAsync(declared, ct);

        logger.LogInformation(
            "Pronunciation rule removed pattern={Pattern} word={Word}",
            LogSanitize.Strip(removed.Pattern), LogSanitize.Strip(removed.Word));

        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-reads the just-written state and returns the ONE response for a POST/PUT — the row whose
    /// resolved (Pattern, Word) identity is <paramref name="resolved"/>. Uniqueness is enforced before
    /// either caller ever writes, so this row always exists after a successful write; the 500 branch
    /// is an unreached defensive floor, not a designed outcome.
    /// </summary>
    async Task<IActionResult> CreatedOrUpdatedRowAsync(PronunciationRule resolved, bool created, CancellationToken ct)
    {
        await activePersonaPronunciations.RefreshIfStaleAsync(ct);
        var row = BuildRows().FirstOrDefault(r =>
            r.Source == "station"
            && string.Equals(r.Pattern, resolved.Pattern, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Word, resolved.Word, StringComparison.OrdinalIgnoreCase));

        if (row is null)
            return StatusCode(StatusCodes.Status500InternalServerError);

        return created ? StatusCode(StatusCodes.Status201Created, row) : Ok(row);
    }

    /// <summary>
    /// Builds the merged station∪persona view fresh (SPEC F97.3, T142 review ruling: "Snapshot() is
    /// your read seam; join it against a fresh merge at request time") — station rules are read and
    /// compiled from the CURRENT <c>Tts:Pronunciations</c> value in THIS call (never the cached
    /// <c>PronunciationRuleProvider</c> singleton the render path uses).
    /// </summary>
    List<PronunciationRuleDto> BuildRows()
    {
        var declared = ReadStationRules();
        var stationSet = PronunciationRuleSet.Create(declared);
        var cardSet = PronunciationRuleSet.Create(activePersonaPronunciations.Current);
        var merged = PronunciationRuleSet.MergeWithProvenance(stationSet, cardSet);

        // Snapshot() already keys on (pattern, word) identity (PronunciationRuleHitStats' own
        // ConcurrentDictionary), so every entry here is already distinct — a plain ToDictionary, no
        // grouping needed. OrdinalIgnoreCase on the dictionary itself (T144 review finding F7) — not
        // a manual ToUpperInvariant fold on each key, which duplicates what the comparer already does
        // and risks the two sides folding differently.
        var hits = hitStats.Snapshot()
            .ToDictionary(hit => HitKey(hit.Pattern, hit.Word), hit => hit.Fired, StringComparer.OrdinalIgnoreCase);

        var rows = new List<PronunciationRuleDto>(merged.Count);
        foreach (var entry in merged)
        {
            var isStation = entry.Source == PronunciationRuleSource.Station;
            var hitCount = entry.InEffect && hits.TryGetValue(HitKey(entry.Rule.Pattern, entry.Rule.Word), out var fired)
                ? fired
                : (long?)null;

            rows.Add(new PronunciationRuleDto(
                entry.Rule.Pattern, entry.Rule.Word, entry.Rule.Ipa,
                isStation ? "station" : "persona", entry.InEffect, hitCount, Reason: null));
        }

        // T144 review finding F3: a declared station rule PronunciationRuleSet.Create silently drops
        // (blank pattern/word/ipa, an ipa carrying ')'/'['/']', a word not found inside its own
        // pattern) never reaches the compiled `merged` set above — without this, the operator sees an
        // empty list over a non-empty Tts:Pronunciations setting. Named via the SAME validator the
        // write path uses, so "why did this never fire" and "why was my save refused" always agree.
        foreach (var raw in declared)
        {
            var errors = PronunciationRuleValidator.Validate(raw.Pattern ?? "", raw.Word, raw.Ipa ?? "");
            if (errors.Count == 0)
                continue; // already represented above — this one compiled cleanly

            var resolved = PronunciationRule.Parse(raw.Pattern ?? "", raw.Word, raw.Ipa ?? "");
            rows.Add(new PronunciationRuleDto(
                resolved.Pattern, resolved.Word, raw.Ipa ?? "", "station", InEffect: false,
                HitCount: null, Reason: string.Join(" ", errors.Select(e => e.Message))));
        }

        return rows;
    }

    /// <summary>
    /// Reads and deserializes the CURRENT <c>Tts:Pronunciations</c> value into its declared (raw, not
    /// yet compiled) rule list, through the SAME <see cref="PronunciationRuleJson.ParseDeclared"/> seam
    /// <see cref="PronunciationRuleProvider"/> uses (T144 review finding F4) — a narrower catch here
    /// could 500 on exactly the malformed input the render path degrades from with a WARN. Null array
    /// elements are filtered for THIS controller's own array-manipulation purposes (add/remove/find);
    /// the shared seam itself does not filter them, so <c>PronunciationRuleProvider</c>'s own
    /// declared-vs-compiled count stays accurate.
    /// </summary>
    List<PronunciationRule> ReadStationRules()
    {
        var (rules, fault) = PronunciationRuleJson.ParseDeclared(pronunciationOptions.CurrentValue.Pronunciations);
        if (fault is not null)
        {
            logger.LogWarning(
                fault, "Tts:Pronunciations could not be parsed; no station pronunciation rules applied until it is fixed");
        }

        return rules.Where(rule => rule is not null).ToList();
    }

    Task WriteStationRulesAsync(IReadOnlyList<PronunciationRule> rules, CancellationToken ct) =>
        store.WriteAsync(SettingKey, PronunciationRuleJson.Serialize(rules), ct);

    /// <summary>
    /// Finds the position in <paramref name="declared"/> whose RESOLVED (Pattern, Word) identity
    /// (case-insensitive — the same identity <see cref="PronunciationRuleSet.Merge"/>,
    /// <see cref="PronunciationRuleHitStats"/>, and this controller's own uniqueness check all key on)
    /// matches <paramref name="pattern"/>/<paramref name="word"/>. Resolving each declared entry's own
    /// word-default before comparing means a caller can address a rule that omitted <c>word</c> in
    /// storage by its resolved (pattern-defaulted) identity — the same identity <see cref="BuildRows"/>
    /// already shows it under.
    /// </summary>
    static int? FindDeclaredIndexByIdentity(IReadOnlyList<PronunciationRule> declared, string pattern, string word)
    {
        for (var i = 0; i < declared.Count; i++)
        {
            var resolved = PronunciationRule.Parse(declared[i].Pattern ?? "", declared[i].Word, declared[i].Ipa ?? "");
            if (string.Equals(resolved.Pattern, pattern, StringComparison.OrdinalIgnoreCase)
                && string.Equals(resolved.Word, word, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return null;
    }

    // Delimits a (pattern, word) hit-count lookup key — a control character no operator-authored
    // pattern/word plausibly contains, so the two fields can never collide across the join (e.g.
    // Pattern="A B" Word="C" vs. Pattern="A" Word="B C"). Paired with StringComparer.OrdinalIgnoreCase
    // on the dictionary itself (BuildRows) rather than a manual case fold on the key (T144 review
    // finding F7).
    const char KeySeparator = '\x1F';

    static string HitKey(string pattern, string word) => $"{pattern}{KeySeparator}{word}";

    static ValidationProblemDetails InvalidRuleProblem(IReadOnlyList<PronunciationRuleValidationError> errors)
    {
        var problem = new ValidationProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more pronunciation rule fields are invalid.",
        };
        foreach (var group in errors.GroupBy(e => e.Field, StringComparer.OrdinalIgnoreCase))
            problem.Errors[group.Key] = group.Select(e => e.Message).ToArray();

        return problem;
    }

    static ProblemDetails DuplicateRuleProblem(string pattern, string word) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = "A pronunciation rule with this pattern and word already exists.",
        Detail = $"An existing station rule already matches pattern '{pattern}' word '{word}'.",
    };

    static ProblemDetails NotFoundProblem(string pattern, string word) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title = "Pronunciation rule not found.",
        Detail = $"No station pronunciation rule matches pattern '{pattern}' word '{word}'.",
    };
}
