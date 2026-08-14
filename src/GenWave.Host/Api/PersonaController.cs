using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;
using GenWave.Host.Options;
using GenWave.Tts;

namespace GenWave.Host.Api;

/// <summary>
/// DJ persona CRUD for the Admin UI (SPEC F35.4, STORY-120): <c>GET/POST/PATCH/DELETE /api/personas</c>
/// over <see cref="IPersonaStore"/>. Also serves the portable card export (SPEC F79.1, STORY-208,
/// PLAN T66): <c>GET /api/personas/{slug}/export</c>.
///
/// DELIBERATE F18.6 DEVIATION — NO <c>If-Match</c>/ETag anywhere in this controller. Every other
/// admin write surface (media tags, library rename) carries optimistic concurrency because a
/// background enricher can race an operator edit; personas have a single writer (the operator, via
/// this controller) and no background contender — there is nothing for an ETag to protect against.
/// A reviewer seeing If-Match reintroduced here should ask why: it would be ceremony with no
/// concurrency bug behind it (SPEC F35.4, ARCHITECTURE "Personas (F35)").
///
/// Security: deny-by-default cookie auth (same fallback policy as every other <c>/api/*</c>
/// controller). Writes require <c>Content-Type: application/json</c> (<see cref="ConsumesAttribute"/>
/// — 415 otherwise, F18.7).
/// </summary>
[ApiController]
[Route("api/personas")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed partial class PersonaController(
    IPersonaStore personaStore,
    IOptionsMonitor<StationOptions> stationMonitor,
    IPersonaPreviewWriter previewWriter,
    IActivePersonaAccessor personaAccessor,
    IAdminMediaLookup mediaLookup,
    IStationScopeProvider scopeProvider,
    IPersonaMemory personaMemory,
    IPersonaTasteReader personaTaste,
    IPersonaImportStore personaImportStore,
    ITtsVoiceLister voiceLister,
    IStationClockProvider stationClock,
    ILogger<PersonaController> logger) : ControllerBase
{
    /// <summary>GET /api/personas — every persona row, ordered by name (F35.4). Each row is joined
    /// with its F71.1 card (one batch query, gh-#256) so the Admin UI can show a catalog-hired DJ's
    /// soul/quirks/lore — the fields its blank legacy backstory/style columns can't carry.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var personas = await personaStore.GetAllAsync(ct);
        var cards = await personaStore.GetCardsAsync(ct);
        return Ok(personas.Select(p => ToDto(p, cards.GetValueOrDefault(p.Id))).ToArray());
    }

    /// <summary>
    /// POST /api/personas — create a persona. 201 with the row on success; 400 for a blank/missing
    /// name; 409 for a duplicate name (F35.4).
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Create([FromBody] PersonaRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(BlankNameProblem());

        var draft = ToDraft(request);
        var result = await personaStore.CreateAsync(draft, ct);

        if (result is PersonaWriteResult.Created created)
            logger.LogInformation(
                "Persona created id={PersonaId} name={PersonaName}",
                created.Persona.Id, LogSanitize.Strip(created.Persona.Name));

        return result switch
        {
            PersonaWriteResult.Created c => StatusCode(
                StatusCodes.Status201Created, ToDto(c.Persona, await personaStore.GetCardByIdAsync(c.Persona.Id, ct))),
            PersonaWriteResult.NameConflict => Conflict(NameConflictProblem(draft.Name)),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// PATCH /api/personas/{id} — edit an existing persona. 200 with the row on success; 400 for a
    /// blank/missing name; 404 for an unknown id; 409 for a duplicate name (F35.4). No
    /// <c>If-Match</c> — see the class header.
    /// </summary>
    [HttpPatch("{id:long}")]
    [Consumes("application/json")]
    public async Task<IActionResult> Update(long id, [FromBody] PersonaRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(BlankNameProblem());

        var draft = ToDraft(request);
        var result = await personaStore.UpdateAsync(id, draft, ct);

        if (result is PersonaWriteResult.Updated updated)
            logger.LogInformation(
                "Persona updated id={PersonaId} name={PersonaName}",
                updated.Persona.Id, LogSanitize.Strip(updated.Persona.Name));

        return result switch
        {
            PersonaWriteResult.Updated u => Ok(ToDto(u.Persona, await personaStore.GetCardByIdAsync(u.Persona.Id, ct))),
            PersonaWriteResult.NotFound => NotFound(NotFoundProblem(id)),
            PersonaWriteResult.NameConflict => Conflict(NameConflictProblem(draft.Name)),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// DELETE /api/personas/{id} — remove a persona. 204 on success; 404 for an unknown id; 409,
    /// naming every offending day/time slot AND every offending dated special, when the persona still
    /// appears in the format-clock schedule or a dated special (SPEC F91.9, F120.1 — supersedes
    /// F35.5's retired delete-clears-active write: <c>Station:Persona:ActiveId</c> no longer exists
    /// for a delete to clear).
    ///
    /// <para>
    /// PLAN T121: <see cref="IPersonaStore.DeleteAsync"/> queries <c>station.segment_schedule</c>
    /// BEFORE attempting the delete, so <see cref="PersonaWriteResult.ScheduledElsewhere"/> arrives
    /// here already carrying its <see cref="PersonaWriteResult.ScheduledElsewhere.Slots"/> — the FK
    /// RESTRICT is only the store's race backstop now, never the primary signal (house precedent: the
    /// store, never this controller, is where a raw Postgres SQLSTATE becomes a
    /// <see cref="PersonaWriteResult"/> case; mirrors <see cref="PersonaWriteResult.NameConflict"/>'s
    /// own unique_violation mapping).
    /// </para>
    ///
    /// <para>
    /// gh-#462 (mirrors <c>ShowsController.Delete</c>'s own PLAN T259 extension): the same pre-query
    /// now ALSO reaches <c>station.schedule_special</c>, so a persona referenced ONLY by a dated
    /// special (<c>station.schedule_special.persona_id</c>, db/36's own identical
    /// <c>ON DELETE RESTRICT</c>) arrives here with a populated
    /// <see cref="PersonaWriteResult.ScheduledElsewhere.Specials"/> instead of the old
    /// empty-<c>Slots</c>-and-no-detail case. This action's only job for the
    /// <c>ScheduledElsewhere</c> case is formatting BOTH lists into the 409 <c>Detail</c> an operator
    /// can read at a glance — weekly slots via <see cref="ScheduledSlotText.FormatSlot"/>, dated
    /// specials via <see cref="ScheduledSlotText.FormatSpecial(ScheduledSpecialSlot)"/>.
    /// <see cref="ScheduledPersonaProblem"/>'s generic fallback wording now survives only for the
    /// genuinely-empty race case — both lists still empty after the backstop's own re-query of both
    /// tables — see that method's own remarks.
    /// </para>
    /// </summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var result = await personaStore.DeleteAsync(id, ct);

        if (result is PersonaWriteResult.Deleted)
            logger.LogInformation("Persona deleted id={PersonaId}", id);
        else if (result is PersonaWriteResult.ScheduledElsewhere scheduled)
            logger.LogWarning(
                "Persona delete blocked: id={PersonaId} slotCount={SlotCount} specialCount={SpecialCount}",
                id, scheduled.Slots.Count, scheduled.Specials.Count);

        return result switch
        {
            PersonaWriteResult.Deleted => NoContent(),
            PersonaWriteResult.NotFound => NotFound(NotFoundProblem(id)),
            PersonaWriteResult.ScheduledElsewhere scheduled =>
                Conflict(ScheduledPersonaProblem(id, scheduled.Slots, scheduled.Specials)),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// POST /api/personas/preview — auditions a persona's copy through the REAL
    /// <see cref="IPersonaPreviewWriter"/> (SPEC F35.6, STORY-123): 200 <c>{ text }</c> on success;
    /// 502 ProblemDetails on any LLM failure — NEVER a silently-substituted template (that would
    /// misrepresent the persona being auditioned); 503 ProblemDetails (+ Retry-After) when the
    /// single-flight LLM gate is busy with an on-air render past the preview's bounded queue wait
    /// (<c>Llm:PreviewQueueWaitSeconds</c>) — decline fast, never park the operator behind a
    /// render-ahead burst. The persona previewed is, in order: a saved
    /// persona by <c>personaId</c> (400 if unknown — it is a body field, not a route id, so this
    /// mirrors the 400 the other body-field validations in this controller already use rather than
    /// 404); an unsaved draft built from whichever of <c>name</c>/<c>backstory</c>/<c>style</c>/
    /// <c>voice</c> are present; or, when neither is given, whatever persona is active right now
    /// (resolved through the exact same <see cref="IActivePersonaAccessor"/> seam the on-air writer
    /// itself uses — the natural default).
    ///
    /// <c>mediaId</c> is optional; when present it is resolved through the SAME
    /// <see cref="IAdminMediaLookup"/> + <see cref="IStationScopeProvider"/> pair
    /// <c>MediaController.GetById</c> uses (400 unknown id, 403 out-of-scope) — this is a read of
    /// track metadata for prompt building, not a new authorization surface, so it reuses the
    /// existing IDOR-safe posture rather than inventing a scope-free "ratings-style" read. Absent
    /// <c>mediaId</c> yields a null-<c>Track</c> segment request; both the template renderer and
    /// <c>LlmCopyWriter</c>'s prompt builder already handle that.
    /// </summary>
    [HttpPost("preview")]
    [Consumes("application/json")]
    public async Task<IActionResult> Preview([FromBody] PersonaPreviewRequest request, CancellationToken ct)
    {
        if (!TryParseKind(request.Kind, out var kind))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "Invalid kind.",
                Detail = $"kind must be one of: {string.Join(", ", Enum.GetNames<SegmentKind>())}.",
            });
        }

        var (persona, personaError) = await ResolvePreviewPersonaAsync(request, ct);
        if (personaError is not null)
            return personaError;

        var (track, trackError) = await ResolveTrackAsync(request.MediaId, ct);
        if (trackError is not null)
            return trackError;

        var station = stationMonitor.CurrentValue;
        // gh-#117 — a preview prompt's LocalNow rides the same station clock the on-air
        // Orchestrator stamps, so auditioning a persona shows the DJ's real wall clock.
        var segmentRequest = new SegmentRequest(
            kind, ResolvePreviewVoice(persona, station.Voice), station.Name, track, stationClock.LocalNow, station.Id);

        var result = await previewWriter.WritePreviewAsync(segmentRequest, persona, ct);

        return result switch
        {
            PersonaPreviewResult.Success s => Ok(new PersonaPreviewResponse(s.Text)),
            PersonaPreviewResult.Busy => LlmBusyProblem(),
            PersonaPreviewResult.Failed f => StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title  = "Persona preview failed.",
                Detail = f.Detail,
            }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// 503 + Retry-After for a preview declined because the single-flight LLM gate is held by an
    /// on-air render: the writer waited <c>Llm:PreviewQueueWaitSeconds</c>, then gave up rather than
    /// queueing the operator behind a render-ahead burst (which is what used to surface as an
    /// opaque proxy-timeout 500). The title is the operator-facing message — the Admin UI toasts
    /// ProblemDetails titles (F35.7) — and Retry-After matches one fenced-CPU generation, so "a
    /// moment" is honest.
    /// </summary>
    IActionResult LlmBusyProblem()
    {
        Response.Headers.RetryAfter = "30";
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title  = "The LLM is busy with an on-air render — try again in a moment.",
            Detail = "Previews wait briefly for the LLM, then decline rather than queue behind on-air segment renders.",
        });
    }

    /// <summary>
    /// GET /api/personas/{slug}/export — the portable card (SPEC F79.1, F79.2; STORY-208): the
    /// stored <c>persona.definition</c> with its <c>lore[]</c>/<c>taste[]</c> REPLACED by a fresh,
    /// source-filtered read of <c>persona_memory</c>/<c>persona_taste</c> (<c>source='authored'</c>
    /// only) — never the stored definition's own (vestigial, F71.2 always-empty-at-migration)
    /// <c>lore</c> field, and never an unfiltered read trimmed down afterward in this method. Zero
    /// accrued/operator rows reach the response BY CONSTRUCTION: <see cref="IPersonaMemory.ListAsync"/>
    /// and <see cref="IPersonaTasteReader.ListAsync"/> are called with the source fixed to
    /// <see cref="PersonaMemorySource.Authored"/>/<see cref="PersonaTasteSource.Authored"/> — there is
    /// no unfiltered overload reachable from this action for a future edit to regress into. 404 for an
    /// unknown slug (F79.1, AC4); the response's <c>Content-Disposition</c> names the file
    /// <c>&lt;slug&gt;.persona.json</c> verbatim via <see cref="ControllerBase.File(byte[], string, string)"/>
    /// (safe filename-header encoding — never a hand-built header string).
    /// </summary>
    [HttpGet("{slug}/export")]
    public async Task<IActionResult> Export(string slug, CancellationToken ct)
    {
        var id = await personaStore.GetIdBySlugAsync(slug, ct);
        if (id is null)
            return NotFound(UnknownSlugProblem(slug));

        var card = await personaStore.GetCardByIdAsync(id.Value, ct);
        if (card is null)
            return NotFound(UnknownSlugProblem(slug));

        var lore = await personaMemory.ListAsync(id.Value, PersonaMemorySource.Authored, ct);
        var taste = await personaTaste.ListAsync(id.Value, PersonaTasteSource.Authored, ct);

        var exportCard = card with
        {
            Lore = lore.Select(entry => entry.Content).ToArray(),
            Taste = taste.Select(entry => entry.Rule).ToArray(),
        };

        var json = PersonaCardSerializer.Serialize(exportCard);
        return File(Encoding.UTF8.GetBytes(json), "application/json", $"{slug}.persona.json");
    }

    /// <summary>
    /// GET /api/personas/{id}/taste — the persona's taste rules grouped by source, plus the accrued
    /// count against the cap (SPEC F86.6, STORY-219, PLAN T77). Read-only BY CONSTRUCTION: this is
    /// the only HTTP verb this controller maps under <c>{id}/taste</c>, and this release adds no
    /// taste write surface beyond the existing thumb endpoint
    /// (<see cref="BoothLogController.ThumbTaste"/>, SPEC F84.1) — an inspector, never a second
    /// mutation path. Unlike <see cref="Export"/>'s deliberately Authored-only, source-filtered
    /// <see cref="IPersonaTasteReader.ListAsync"/> call, this route calls it with
    /// <c>source: null</c> — every source, on purpose, since grouping ALL of them for the operator to
    /// inspect is the entire point (F86.9: admin-plane only, never reachable from a spectator
    /// surface — this controller's class-level <see cref="AdminSurfaceAttribute"/>/AdminOnly policy
    /// covers this action the same as every other one here). 404 for an unknown persona id.
    /// </summary>
    [HttpGet("{id:long}/taste")]
    public async Task<IActionResult> Taste(long id, CancellationToken ct)
    {
        var persona = await personaStore.GetByIdAsync(id, ct);
        if (persona is null)
            return NotFound(NotFoundProblem(id));

        var rules = await personaTaste.ListAsync(id, source: null, ct);

        return Ok(new PersonaTasteResponseDto(
            RulesBySource(rules, PersonaTasteSource.Authored),
            RulesBySource(rules, PersonaTasteSource.Operator),
            RulesBySource(rules, PersonaTasteSource.Accrued),
            rules.Count(r => r.Source == PersonaTasteSource.Accrued),
            IPersonaTasteAccrualStore.Cap));
    }

    /// <summary>
    /// POST /api/personas/{slug}/import — upserts a persona from a portable <c>&lt;slug&gt;.persona.json</c>
    /// card (SPEC F79.2, F79.3, F79.4, F79.6, F90.7; STORY-209, STORY-237, PLAN T67, T98). Every gate
    /// below runs BEFORE <see cref="IPersonaImportStore.ImportAsync"/> — the one transactional write —
    /// so a rejection at ANY of them means nothing was ever written (F79.6):
    /// <list type="number">
    /// <item>Slug format: the same lowercase/digit/single-hyphen shape
    /// <c>LegacyPersonaCardMapper.Slugify</c> ever PRODUCES, checked here as a REJECT rather than a
    /// silent auto-correct — a bad slug in an import request is an operator/tooling error worth
    /// surfacing, not fixing up quietly.</item>
    /// <item><paramref name="catalogSlug"/> format (F90.7): checked against the SAME
    /// <see cref="SlugFormat"/> rule as the route slug above, when present — never silently dropped or
    /// coerced.</item>
    /// <item>Payload size: capped at <see cref="BoundedImportBodyReader.MaxImportBytes"/>, enforced by
    /// <see cref="BoundedImportBodyReader.ReadBoundedBodyAsync"/> reading the body itself with a
    /// running-total guard — see that method's remarks for why
    /// <see cref="RequestSizeLimitAttribute"/> alone is not enough.</item>
    /// <item>Deserialization IS the validation (F71.1, F79.6): <see cref="PersonaCardSerializer.Deserialize"/>
    /// is the only parse of the body, ever. A syntactically malformed body
    /// (<see cref="JsonException"/>) or an in-range-looking-but-invalid field — e.g. a
    /// <c>TasteRule.Weight</c> outside <c>[-1, 1]</c>, which throws <see cref="ArgumentOutOfRangeException"/>
    /// from inside that record's own constructor on every construction path including this one
    /// (carried PLAN T67 review note) — both map to 400, never an unhandled 500.</item>
    /// <item>Schema major (F79.2): a card whose <see cref="PersonaCard.SchemaVersion"/> exceeds
    /// <see cref="PersonaCard.CurrentSchemaVersion"/> is refused, the message naming both.</item>
    /// </list>
    /// Voice resolution (F79.4, <see cref="ResolveVoiceAsync"/>) runs after every gate above passes,
    /// still before the write — its outcome (the legacy voice column to persist, plus any warning)
    /// feeds straight into the <see cref="PersonaImportRequest"/> the write receives. The write's own
    /// only failure mode, a name collision, maps to 409 — the same status every other write action on
    /// this controller already uses for <see cref="PersonaWriteResult.NameConflict"/>.
    ///
    /// <para>
    /// Pace range (gh-#483, PLAN T140 follow-up, <see cref="PaceWarning"/>): <see cref="TtsPace"/>'s
    /// render-time clamp already keeps an out-of-range <c>card.Voice.Pace</c> (e.g. <c>3.0</c>) safe
    /// to send to the engine, silently — ruled right there, since <see cref="ActivePersonaPaceCache"/>'s
    /// 30s poll would otherwise re-log the same standing value forever. That silence leaves the
    /// operator with no signal ANYWHERE, so the check runs a second time here, once, at import/save —
    /// never refusing the write, only appending a warning to the same <see cref="PersonaImportResponse.Warnings"/>
    /// channel <see cref="ResolveVoiceAsync"/>'s own unresolved-voice case already uses.
    /// </para>
    ///
    /// <paramref name="catalogSlug"/> (SPEC F90.7, STORY-237, PLAN T98, T103) is the ONLY provenance
    /// signal this action passes to the store: present and valid ⇒
    /// <see cref="PersonaImportRequest.ImportedFrom"/> is the catalog entry's own slug; absent ⇒ the
    /// request's <see cref="PersonaImportRequest.ImportedFrom"/> default of
    /// <see cref="PersonaImportRequest.FileSource"/> applies unchanged, so every caller that predates
    /// F90 (a plain file upload, including every existing test in Story209_PersonaImport.cs) behaves
    /// exactly as before.
    /// </summary>
    [HttpPost("{slug}/import")]
    [Consumes("application/json")]
    [RequestSizeLimit(BoundedImportBodyReader.MaxImportBytes)]
    public async Task<IActionResult> Import(string slug, [FromQuery] string? catalogSlug, CancellationToken ct)
    {
        if (!SlugFormat().IsMatch(slug))
            return BadRequest(BadSlugProblem(slug));

        if (!string.IsNullOrEmpty(catalogSlug))
        {
            // Length bound BEFORE the regex (cheap reject; also keeps a pathological input away from
            // the regex engine at all) — matches SPEC F90.7's own "the entry slug" contract: a real
            // catalog entry slug is a short, human-authored identifier, never anywhere near this long.
            if (catalogSlug.Length > BoundedImportBodyReader.MaxCatalogSlugLength)
                return BadRequest(CatalogSlugTooLongProblem(catalogSlug.Length));

            if (!SlugFormat().IsMatch(catalogSlug))
                return BadRequest(BadCatalogSlugProblem(catalogSlug));
        }

        var (json, oversized) = await BoundedImportBodyReader.ReadBoundedBodyAsync(
            Request, BoundedImportBodyReader.MaxImportBytes, ct);
        if (oversized)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, OversizedProblem());

        PersonaCard? card;
        try
        {
            card = PersonaCardSerializer.Deserialize(json);
        }
        catch (JsonException ex)
        {
            return BadRequest(MalformedCardProblem(ex.Message));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(MalformedCardProblem(ex.Message));
        }

        if (card is null)
            return BadRequest(MalformedCardProblem("The payload deserialized to no card."));

        if (card.SchemaVersion > PersonaCard.CurrentSchemaVersion)
            return BadRequest(NewerSchemaProblem(card.SchemaVersion));

        var (legacyVoice, voiceWarnings) = await ResolveVoiceAsync(card.Voice, ct);
        IReadOnlyList<string> warnings = PaceWarning(card.Voice.Pace) is { } paceWarning
            ? [.. voiceWarnings, paceWarning]
            : voiceWarnings;

        var importedFrom = string.IsNullOrEmpty(catalogSlug) ? PersonaImportRequest.FileSource : catalogSlug;
        var outcome = await personaImportStore.ImportAsync(
            new PersonaImportRequest(slug, legacyVoice, card, importedFrom), ct);

        if (outcome is PersonaImportOutcome.Imported succeeded)
            logger.LogInformation(
                "Persona imported slug={Slug} id={PersonaId} created={WasCreated} warnings={WarningCount}",
                slug, succeeded.PersonaId, succeeded.WasCreated, warnings.Count);

        return outcome switch
        {
            PersonaImportOutcome.Imported { WasCreated: true } imported =>
                StatusCode(StatusCodes.Status201Created, new PersonaImportResponse(imported.PersonaId, slug, card.Name, warnings)),
            PersonaImportOutcome.Imported imported =>
                Ok(new PersonaImportResponse(imported.PersonaId, slug, card.Name, warnings)),
            PersonaImportOutcome.NameConflict => Conflict(NameConflictProblem(card.Name)),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves <paramref name="voice"/> against this station's live TTS voice list (SPEC F79.4).
    /// <see cref="VoiceSpec.Engine"/> plays no part in the check: this codebase has exactly one
    /// configurable TTS backend at a time (<see cref="ITtsVoiceLister"/> lists "the voice ids
    /// installed on the configured backend", singular — there is no per-engine dimension to route on
    /// yet), so only <see cref="VoiceSpec.VoiceId"/> membership is checked. An empty
    /// <see cref="VoiceSpec.VoiceId"/> already means "use the station default" and always resolves,
    /// with no lookup at all.
    ///
    /// ENGINE-DOWN POSTURE (deliberate call, PLAN T67): a fault from <see cref="voiceLister"/> resolves
    /// the card's voice AS GIVEN, with NO warning — never "can't verify, so warn anyway". F79.4 exists
    /// so import succeeds on a stranger's station; a fault here most often means the TTS container is
    /// mid-boot (this project's own compose bring-up ordering), and crying "unresolved" on a voice
    /// that is actually installed the moment the container finishes starting would be a false alarm on
    /// every import performed during that window — the more astonishing failure mode, not the less. A
    /// voice that genuinely never resolves gets its warning the next time this route runs with the
    /// engine reachable.
    /// </summary>
    async Task<(string LegacyVoice, IReadOnlyList<string> Warnings)> ResolveVoiceAsync(VoiceSpec voice, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(voice.VoiceId))
            return (string.Empty, []);

        IReadOnlyList<string> voices;
        try
        {
            voices = await voiceLister.ListVoicesAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Voice list unreachable during persona import — accepting the card's voice unverified");
            return (voice.VoiceId, []);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Voice list timed out during persona import — accepting the card's voice unverified");
            return (voice.VoiceId, []);
        }

        if (voices.Contains(voice.VoiceId, StringComparer.Ordinal))
            return (voice.VoiceId, []);

        return (string.Empty,
            [$"Voice \"{voice.VoiceId}\" is not available on this station; using the station default voice instead."]);
    }

    /// <summary>
    /// The operator-visible sibling of <see cref="TtsPace"/>'s own render-time clamp (gh-#483, PLAN
    /// T140 follow-up): a finite <paramref name="pace"/> outside <see cref="TtsPace.MinSpeed"/>/
    /// <see cref="TtsPace.MaxSpeed"/> still renders fine (T140's clamp handles that), but silently —
    /// this names the authored value AND the bound it will actually render at, once, here, rather than
    /// leaving the operator to notice only by ear. Uses <see cref="TtsPace.Clamp"/> itself for "is this
    /// in range", never a duplicated <c>[0.5, 2.0]</c> literal.
    ///
    /// A <see cref="TtsPace.IsDegenerate"/> value (<c>NaN</c>/<c>Infinity</c>/zero/negative) is
    /// deliberately OUT of scope here: T140's own <see cref="ActivePersonaPaceCache"/> already WARNs
    /// (latched, so it never repeats) the first time a persona with that value goes live — this method
    /// only covers the honest-but-out-of-range case this issue names (e.g. <c>3.0</c>), which that
    /// latch does not cover at all.
    /// </summary>
    static string? PaceWarning(double pace)
    {
        if (TtsPace.IsDegenerate(pace))
            return null;

        var clamped = TtsPace.Clamp(pace);
        if (clamped == pace)
            return null;

        return
            $"Pace {pace:0.0###} is outside this engine's [{TtsPace.MinSpeed:0.0###}, {TtsPace.MaxSpeed:0.0###}] " +
            $"window and will render at {clamped:0.0###}.";
    }

    // Mirrors LegacyPersonaCardMapper.Slugify's own character class (lowercase letters, digits,
    // single hyphens) expressed as a REJECT rather than a TRANSFORM — Slugify exists to turn an
    // arbitrary persona name into a legal slug; this exists to refuse an illegal one arriving over
    // the wire, never to silently fix it up.
    //
    // \A/\z, NOT ^/$ (security-api finding): .NET regex `$` matches immediately before a trailing
    // '\n' (not just at the true end of input), so `^[a-z0-9]+(-[a-z0-9]+)*$` let a value like
    // "dj-nova\n" — or, over the wire, "?catalogSlug=x%0A" — through this gate. \A/\z anchor to the
    // absolute start/end of the string with no such exception.
    [GeneratedRegex("\\A[a-z0-9]+(-[a-z0-9]+)*\\z")]
    private static partial Regex SlugFormat();

    /// <summary>
    /// Resolves the persona <see cref="Preview"/> should use, per the three-way precedence
    /// documented on <see cref="Preview"/>'s remarks. Returns a non-null <see cref="IActionResult"/>
    /// error only for an unknown <c>personaId</c>; every other path always succeeds (a draft persona
    /// can always be built, and the active-persona accessor never throws).
    /// </summary>
    async Task<(Persona? Persona, IActionResult? Error)> ResolvePreviewPersonaAsync(
        PersonaPreviewRequest request, CancellationToken ct)
    {
        if (request.PersonaId is { } personaId)
        {
            var found = await personaStore.GetByIdAsync(personaId, ct);
            if (found is null)
                return (null, BadRequest(UnknownPersonaProblem(personaId)));

            return (found, null);
        }

        if (HasDraftFields(request))
        {
            var now = DateTime.UtcNow;
            var name = string.IsNullOrWhiteSpace(request.Name) ? "Draft persona" : request.Name.Trim();
            return (new Persona(
                0, name, request.Backstory ?? string.Empty, request.Style ?? string.Empty,
                request.Voice ?? string.Empty, now, now), null);
        }

        // Neither personaId nor draft fields — preview whatever persona is live on-air right now,
        // through the exact seam the LLM writer itself resolves it through (F35.6's natural default).
        return (await personaAccessor.ResolveAsync(ct), null);
    }

    static bool HasDraftFields(PersonaPreviewRequest request) =>
        !string.IsNullOrEmpty(request.Name) || !string.IsNullOrEmpty(request.Backstory) ||
        !string.IsNullOrEmpty(request.Style) || !string.IsNullOrEmpty(request.Voice);

    /// <summary>
    /// Resolves an optional <c>mediaId</c> to a preview-only <see cref="MediaItem"/> via the exact
    /// lookup + scope check <c>MediaController.GetById</c> uses (400 unknown, 403 out-of-scope).
    /// Loudness/measurement fields on the returned item are placeholders — this track is never
    /// synthesized or measured; only title/artist/album/genre/year reach the LLM prompt
    /// (<c>LlmCopyWriter.BuildUserContent</c>).
    /// </summary>
    async Task<(MediaItem? Track, IActionResult? Error)> ResolveTrackAsync(long? mediaId, CancellationToken ct)
    {
        if (mediaId is null)
            return (null, null);

        var found = await mediaLookup.GetByIdWithLibraryAsync(mediaId.Value, ct);
        if (found is null)
            return (null, BadRequest(UnknownMediaProblem(mediaId.Value)));

        var (row, libraryId) = found.Value;
        if (!scopeProvider.Current.LibraryIds.Contains(libraryId))
        {
            // Mirrors MediaController.GetById: log the denial without row data (security-api hard
            // rule gitea-#10) and answer 403, not 404 — house default for an existing-but-out-of-scope row.
            logger.LogWarning(
                "Persona preview media access denied: mediaId={MediaId} reason=out_of_scope", mediaId.Value);
            return (null, StatusCode(StatusCodes.Status403Forbidden, new { message = "Access denied." }));
        }

        return (ToPreviewTrack(row), null);
    }

    static MediaItem ToPreviewTrack(AdminMediaDto row) =>
        new(
            row.MediaId,
            row.Locator,
            row.Title ?? row.MediaId,
            // Fully qualified: "Loudness" unqualified resolves to the sibling GenWave.Loudness
            // PROJECT namespace here (both nest under the shared GenWave ancestor), not the
            // Core.Domain struct — the one spot in this file that constructs one directly.
            new Core.Domain.Loudness(row.IntegratedLufs ?? 0, row.TruePeakDbtp ?? 0, row.Measurable ?? false),
            Artist: row.Artist,
            Album: row.Album,
            Genre: row.Genre,
            Year: row.Year);

    static string ResolvePreviewVoice(Persona? persona, string stationVoice) =>
        persona is not null && !string.IsNullOrEmpty(persona.Voice) ? persona.Voice : stationVoice;

    static bool TryParseKind(string? raw, out SegmentKind kind)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            kind = SegmentKind.LeadIn;
            return true;
        }

        return Enum.TryParse(raw, ignoreCase: true, out kind);
    }

    static ProblemDetails UnknownPersonaProblem(long id) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Unknown persona.",
        Detail = $"No persona with id {id} exists.",
    };

    static ProblemDetails UnknownMediaProblem(long id) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Unknown media.",
        Detail = $"No media row with id {id} exists.",
    };

    // gh-#256: the card half of the DTO — a catalog-hired persona's narrative lives in the F71.1
    // card (soul with its embedded "Style:" line, quirks, lore), never the legacy columns. A row
    // with no reconciled card (null) serializes the three as ""/empty, never absent keys.
    static PersonaDto ToDto(Persona persona, PersonaCard? card) =>
        new(
            persona.Id, persona.Name, persona.Backstory, persona.Style, persona.Voice, persona.Slug,
            persona.ImportedFrom, persona.ImportedAt,
            card?.Soul ?? "", card?.Quirks ?? [], card?.Lore ?? []);

    static IReadOnlyList<PersonaTasteRuleDto> RulesBySource(
        IReadOnlyList<PersonaTasteEntry> rules, PersonaTasteSource source) =>
        rules.Where(r => r.Source == source).Select(ToTasteRuleDto).ToArray();

    static PersonaTasteRuleDto ToTasteRuleDto(PersonaTasteEntry entry) => new(
        PredicateSummary(entry.Rule.Predicate),
        entry.Rule.Context.DaysOfWeek,
        entry.Rule.Context.StartHour,
        entry.Rule.Context.EndHour,
        entry.Rule.Weight,
        entry.UpdatedAt);

    /// <summary>
    /// Predicate summary for one taste rule (SPEC F86.6) — the SAME artist-over-genre-over-tag
    /// precedence <see cref="BoothLogFiredRuleSummary.FromTasteRule"/> already established for a
    /// FIRED rule's pick-stamp label, but with its OWN fallback: a taste rule with no predicate field
    /// set at all matches every track, so "any track" reads correctly here — the pick-stamp's "this
    /// pick" (describing one already-resolved airing) and the ranker's own debug-log "any" token
    /// would both read oddly in a standing table of taste opinions. A third documented divergence
    /// (see <see cref="BoothLogFiredRuleSummary"/>'s own remarks for the first two), not a fourth copy
    /// of the precedence logic re-derived by hand.
    /// </summary>
    static string PredicateSummary(TastePredicate predicate) =>
        predicate.LabelOr("any track");

    // Trims the name and defaults the optional fields to "" — mirrors Persona.Voice's "" = station
    // default sentinel for all three optional fields, not just voice. Soul (gh-#256) stays nullable:
    // null/blank means "not editing the soul" (PersonaDraft.Soul's own contract), so a legacy client
    // that never sends the field behaves exactly as before.
    static PersonaDraft ToDraft(PersonaRequest request) =>
        new(
            request.Name?.Trim() ?? string.Empty,
            request.Backstory ?? string.Empty,
            request.Style ?? string.Empty,
            request.Voice ?? string.Empty,
            string.IsNullOrWhiteSpace(request.Soul) ? null : request.Soul);

    static ProblemDetails BlankNameProblem() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Validation error.",
        Detail = "name must not be blank or whitespace.",
    };

    static ProblemDetails NameConflictProblem(string name) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title  = "Name conflict.",
        Detail = $"A persona named \"{name}\" already exists.",
    };

    static ProblemDetails NotFoundProblem(long id) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"No persona with id {id} exists.",
    };

    /// <summary>
    /// SPEC F91.9, F120.1 (PLAN T121, gh-#462) — names every slot AND every dated special blocking
    /// the delete, formatted for an operator to read at a glance:
    /// <c>"Mon 09:00–12:00, 2026-08-20 09:00–12:00"</c> — weekly slots first (in their own
    /// day-then-start-minute order), dated specials after (in their own date-then-start-minute
    /// order), mirroring <c>ShowsController.ReferencedProblem</c>'s own slots-then-specials
    /// concatenation. Both <paramref name="slots"/> and <paramref name="specials"/> are empty only on
    /// the store's race-backstop path (both tables re-queried after one of their FKs fired, but
    /// neither found anything left) — the detail falls back to the T120 scaffolding's original generic
    /// wording for that one genuinely-empty case, since there is nothing left to name.
    /// </summary>
    static ProblemDetails ScheduledPersonaProblem(
        long id, IReadOnlyList<ScheduledSlot> slots, IReadOnlyList<ScheduledSpecialSlot> specials)
    {
        var references = slots.Select(ScheduledSlotText.FormatSlot)
            .Concat(specials.Select(ScheduledSlotText.FormatSpecial))
            .ToList();

        return new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title  = "Persona is scheduled.",
            Detail = references.Count > 0
                ? $"Persona {id} is still scheduled and cannot be deleted: {string.Join(", ", references)}."
                : $"Persona {id} still appears in the format-clock schedule or a dated special and cannot be deleted while scheduled.",
        };
    }

    static ProblemDetails UnknownSlugProblem(string slug) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title  = "Not found.",
        Detail = $"No persona with slug \"{slug}\" exists.",
    };

    static ProblemDetails BadSlugProblem(string slug) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid slug.",
        Detail = $"\"{slug}\" is not a valid persona slug (lowercase letters, digits, and single hyphens only).",
    };

    // SPEC F90.7 — the optional catalogSlug query parameter shares the route slug's own naming rule
    // (SlugFormat), so this names the parameter explicitly rather than reusing BadSlugProblem's route-
    // slug-flavored title, avoiding an operator mistaking one 400 for the other.
    static ProblemDetails BadCatalogSlugProblem(string catalogSlug) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid catalog slug.",
        Detail =
            $"\"{catalogSlug}\" is not a valid catalog slug (lowercase letters, digits, and single hyphens only).",
    };

    static ProblemDetails CatalogSlugTooLongProblem(int length) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Invalid catalog slug.",
        Detail = $"catalogSlug must be at most {BoundedImportBodyReader.MaxCatalogSlugLength} characters (got {length}).",
    };

    static ProblemDetails OversizedProblem() => new()
    {
        Status = StatusCodes.Status413PayloadTooLarge,
        Title  = "Payload too large.",
        Detail = $"Persona cards are capped at {BoundedImportBodyReader.MaxImportBytes / 1024} KB.",
    };

    static ProblemDetails MalformedCardProblem(string detail) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Malformed persona card.",
        Detail = detail,
    };

    static ProblemDetails NewerSchemaProblem(int cardSchemaVersion) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Unsupported schema version.",
        Detail =
            $"Card schema version {cardSchemaVersion} is newer than this station's supported version " +
            $"{PersonaCard.CurrentSchemaVersion}.",
    };
}
