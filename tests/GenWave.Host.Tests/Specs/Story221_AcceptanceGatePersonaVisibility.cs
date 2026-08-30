// STORY-221 — Acceptance gate: persona visibility end-to-end (Epic V24 / SPEC F86, PLAN T81,
// closes STORY-217/218/219/220).
//
// BDD specification — xUnit. T73-T80 (this branch) built the Q4 machinery's read surfaces: the
// booth-log pick stamp (STORY-217), the Live card's shared PickChips (STORY-218), the persona taste
// inspector (STORY-219), and catalog moods (STORY-220). This gate re-affirms F86.9's disclosure
// posture and the epic's zero-diff engine/compose convention independently of any single task's own
// specs — the Story141/147/153/162/212 idiom: every fact below is a real, always-run, non-Skip
// assertion; nothing here shells out to `dotnet test` (that is the CI job's job, not a fact a test
// can assert about itself).
//
// Part 1 (F86.9): every public spectator DTO (the F62.9 set Story183 catalogs) is swept by
// reflection for pick/firedRules/exploration vocabulary — Story217 already pins this for the
// booth-log stamp alone; this gate widens the forbidden vocabulary to taste (F86.6, new this epic)
// and mood (F86.8, new this epic) and re-runs the full sweep, so a persona-taste or catalog-mood
// field landing on a spectator payload by accident fails here even if no other file catches it.
// Alongside the DTO sweep, a real route-table enumeration (WebApplicationFactory<Program>) proves no
// spectator-classified endpoint (SpectatorSurfaceAttribute or the Spectator authorization policy)
// serves a /taste path — Story219 already proves the INVERSE (the taste endpoint isn't tagged
// spectator); this gate proves the forward direction across the whole spectator surface.
//
// Part 2 (Epic V/X zero-diff convention, Story141/147/153/162/212's own idiom): engine/genwave.liq
// and compose.yaml sha256 pins. Verified 2026-07-22 with `git diff origin/main...HEAD -- engine/
// compose.yaml` — empty. T73 was this epic's only schema/write-path change (a Postgres migration
// plus the booth-log write path, both in GenWave.MediaLibrary/db, SPEC F86.1) and, per the V24
// sequencing notes, it is also the only task with any write-side reach; every other task is a read
// projection over data F82-F85 already produce. Both hashes below are BYTE-IDENTICAL to Story212's
// own pin (Epic "Personalities on Air" also left both files untouched) — re-affirmed here, not
// re-derived, because nothing changed.
//
// Part 3 (F86.4, API-side single-source guarantee): the Live card and the booth log both render
// PickChips from the SAME GET /api/booth-log wire shape (admin-ui's own
// useNowPlayingTasteAttribution hook doc comment is explicit: "no second, now-playing-specific
// diagnostics fetch" — LiveController's now-playing/play-history DTOs carry no pick field at all).
// The jest-side proof that the component itself is shared lives in admin-ui's own
// live-card-pick-chips.spec.tsx/booth-log-pick-chips.spec.tsx (STORY-217/218); this xUnit gate pins
// the API side instead: a reflection sweep over every public type in GenWave.Host.Api proves
// BoothLogEntryDto/BoothLogPickDto are the ONLY types anywhere on the surface carrying a
// pick/firedRules-shaped property — no second, independently-shaped wire shape could have drifted in.

using System.Reflection;
using System.Security.Cryptography;
using GenWave.Host.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// Minimal <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's route-table facts —
/// only <see cref="EndpointDataSource"/> metadata is ever inspected, no request is ever sent, so the
/// only setup this needs is removing hosted services that would otherwise attempt real
/// Liquidsoap/Postgres connections at host build time (mirrors Story219_PersonaTasteInspector.cs's
/// own <c>PersonaTasteRouteWebFactory</c> idiom exactly).
/// </summary>
file sealed class PersonaVisibilityGateWebFactory() : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}

public static class FeatureAcceptanceGatePersonaVisibility
{
    /// <summary>Repo root, resolved relative to the test assembly's build output (Story074/102/107/
    /// 141/147/153/162/212's convention).</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string Sha256Hex(string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot, relativePath));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>Every public Spectator-prefixed DTO in <c>GenWave.Host.Api</c> — the F62.9
    /// disclosure-by-construction set Story183_DisclosureContractCompleteness.cs blesses by name,
    /// discovered here the same by-prefix-reflection way (not a hand-maintained list copied from
    /// there), so a brand-new spectator payload is swept in automatically.</summary>
    static IReadOnlyList<Type> SpectatorTypes() =>
        typeof(SpectatorController).Assembly.GetTypes()
            .Where(type => type.IsPublic
                && type.Namespace == "GenWave.Host.Api"
                && type.Name.StartsWith("Spectator", StringComparison.Ordinal))
            .ToList();

    // ---------------------------------------------------------------------
    // PART 2 — Epic V/X zero-diff re-pin convention
    // ---------------------------------------------------------------------

    public static class ScenarioEngineAndComposeCarryZeroDiffFromMain
    {
        // T93 epoch (F88.4 export fix) — settings.encoder.metadata.export now carries "url" too,
        // so the C# feeder's url= annotation actually reaches the ICY StreamUrl (see the
        // Story230 gate's live-run finding).
        //
        // ComposeYamlSha256 re-pinned 2026-07-29 (gh-#241): the piper service moves off the
        // amd64-only artibex/piper-http digest onto the repo-built ./piper build context
        // (published multi-arch per release via the gh-#240 matrix). Same wire shape, env,
        // volume, and healthcheck contract — a piper-service-only edit, another intentional
        // edit from a LATER epic, not a regression of this epic's zero-diff promise.
        // EngineScriptSha256 unchanged — gh-#241 does not touch engine/genwave.liq.
        // BOTH hashes re-pinned 2026-08-17 (gh-#575 follow-up): liquidsoap v2.4.4->v2.4.5 pin
        // comments refreshed in compose.yaml + engine/genwave.liq (prose only, zero functional
        // bytes — the FROM bump itself lives in engine/Dockerfile, outside both pinned files).
        // Another intentional edit from a later epic, not a regression of the zero-diff promise.
        const string EngineScriptSha256 = "36bc56cb9909ed94b475528ab1bb3a2f3d7f7332a5652b5c7e30189955d1358d";
        // ComposeYamlSha256 re-pinned 2026-07-30 (gh-#276): kokoro mem_limit 3g->4g + comment
        // refresh — ops-only edit, no service/wire/volume change. Another intentional edit from
        // a later epic, not a regression of this epic's zero-diff promise.
        // ComposeYamlSha256 re-pinned 2026-08-02 (gh-#334): Library__EnrichmentConcurrency becomes
        // ${LIBRARY_ENRICHMENT_CONCURRENCY:-4} — same default on every existing box, now overridable.
        // A pinned appliance box sets Admin__Enabled=false, closing PUT /api/settings, so the hardcoded
        // value left the operator no lever at all while enrichment pinned all four cores of a Pi 5.
        // Config-indirection only — no service, wire, volume or healthcheck change. Another intentional
        // edit from a later epic, not a regression of this epic's zero-diff promise.
        // ComposeYamlSha256 re-pinned 2026-08-14 (PLAN T148, SPEC F99.2/F99.3, STORY-257): TTS
        // failover becomes opt-in — Tts__Fallback__Endpoint/Tts__Fallback__Voice no longer ship on
        // the api service (the shipped default now resolves an empty fallback chain), and the
        // piper service gains `profiles: ["fallback"]` (off by default; an operator opts in with
        // `--with fallback` plus a live PUT /api/settings). Another intentional edit from a later
        // epic, not a regression of this epic's zero-diff promise.
        // ComposeYamlSha256 re-pinned 2026-08-18 (PLAN T316, SPEC F136.2, STORY-343): api's
        // depends_on: kokoro gains `required: false` — the core `up` no longer blocks/fails on
        // an absent or not-yet-healthy kokoro (staged startup — launch.sh's pinned flow pulls
        // + brings up db/icecast/engine/api[/piper] first, scoped `--no-deps` so kokoro can't
        // ride along via depends_on membership; a second, UNSCOPED pull + `up -d
        // --remove-orphans` (no --no-recreate) then converges kokoro/ollama/ollama-init plus
        // every other profile-gated service). Same T85/T93 epoch-break-and-re-pin ritual; a
        // `depends_on`-only edit — no service, wire, volume or healthcheck change.
        const string ComposeYamlSha256  = "bd61cd11a3c2a8079db63cf1f43618bf7d149d03bee020fc07d2585b04fe6fc7";

        [Fact]
        public static void EngineScriptByteMatchesMain()
        {
            Assert.Equal(EngineScriptSha256, Sha256Hex(Path.Combine("engine", "genwave.liq")));
        }

        [Fact]
        public static void ComposeYamlByteMatchesMain()
        {
            Assert.Equal(ComposeYamlSha256, Sha256Hex("compose.yaml"));
        }
    }

    // ---------------------------------------------------------------------
    // PART 1 — F86.9 spectator-disclosure pins
    // ---------------------------------------------------------------------

    public sealed class ScenarioSpectatorDisclosureBoundary
    {
        [Fact]
        public void NoSpectatorPayloadCarriesPickTasteOrMoodDiagnosticVocabulary()
        {
            // Given every public spectator DTO in the F62.9 disclosure-by-construction set...
            var spectatorTypes = SpectatorTypes();
            Assert.NotEmpty(spectatorTypes);

            // When each type's public instance members are inspected for pick/firedRules/
            // exploration vocabulary (Story217's own forbidden list) PLUS taste (F86.6), mood
            // (F86.8), and nudge (F151.4, STORY-371, MED-5 T370 review) — the rotation-nudge
            // diagnostic added to the booth-log pick stamp/DTO, admin-only exactly like
            // pick/firedRules/taste/mood already are...
            var forbidden = new[] { "pick", "firedrules", "isexploration", "exploration", "taste", "mood", "nudge" };

            // gh-#131 exception, blessed by name: the request form's mood picker made the
            // MoodVocabulary WORD LIST itself deliberately public — SpectatorRequestOptions.Moods
            // offers the fixed vocabulary, SpectatorRequestSubmission.Mood accepts one member back
            // (membership-validated fail-closed, never echoed). That is vocabulary disclosure, not
            // the per-track mood-tag/pick-diagnostic disclosure F86.9 actually forbids — so exactly
            // these two members pass; any OTHER mood-adjacent member still fails here.
            var blessed = new[] { "SpectatorRequestOptions.Moods", "SpectatorRequestSubmission.Mood" };
            var offendingMembers = spectatorTypes
                .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(property => $"{type.Name}.{property.Name}"))
                .Where(name => !blessed.Contains(name, StringComparer.Ordinal))
                .Where(name => forbidden.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // Then none of them expose any of it (F86.9).
            Assert.Empty(offendingMembers);
        }

        [Fact]
        public void NoSpectatorSurfaceEndpointServesATastePath()
        {
            // Given every endpoint classified as spectator surface (SpectatorSurfaceAttribute, the
            // same marker SurfaceGateMiddleware itself keys off, OR the Spectator authorization
            // policy) — the two classification signals Story195/196/219's own admin-vs-spectator
            // facts already rely on...
            using var factory = new PersonaVisibilityGateWebFactory();

            var spectatorEndpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .Where(endpoint =>
                    endpoint.Metadata.GetMetadata<SpectatorSurfaceAttribute>() is not null
                    || endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                        .Any(authorizeData => authorizeData.Policy == AuthorizationPolicies.Spectator))
                .ToList();
            Assert.NotEmpty(spectatorEndpoints);

            // Then none of their route patterns ever reaches a /taste path — persona_taste data
            // (SPEC F86.6) has exactly one route, and it is not this one (F86.9).
            Assert.All(spectatorEndpoints, endpoint =>
                Assert.DoesNotContain(
                    "taste", endpoint.RoutePattern.RawText ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ---------------------------------------------------------------------
    // PART 3 — F86.4 Live<->booth-log chip consistency, pinned API-side
    // ---------------------------------------------------------------------

    public sealed class ScenarioBoothLogIsTheSoleWireShapeForPickData
    {
        [Fact]
        public void OnlyTheBoothLogDtoFamilyExposesPickOrFiredRuleProperties()
        {
            // Given every public type GenWave.Host.Api owns (every controller and DTO on the
            // surface)...
            var hostApiTypes = typeof(SpectatorController).Assembly.GetTypes()
                .Where(type => type.IsPublic && type.Namespace == "GenWave.Host.Api")
                .ToList();
            Assert.NotEmpty(hostApiTypes);

            // When each type's public instance properties are inspected for pick/firedRules/nudge
            // vocabulary (MED-5, T370 review — F151.4's rotation chip rides the SAME BoothLogPickDto
            // this gate already narrows pick-diagnostic vocabulary to)...
            var forbidden = new[] { "pick", "firedrules", "nudge" };
            var owningTypeNames = hostApiTypes
                .Where(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Any(property => forbidden.Any(term =>
                        property.Name.Contains(term, StringComparison.OrdinalIgnoreCase))))
                .Select(type => type.Name)
                .ToHashSet();

            // Then only BoothLogEntryDto.Pick and BoothLogPickDto.FiredRules/Nudge carry it (F86.4,
            // F151.4) — the Live card and the booth log both resolve PickChips from that ONE GET
            // /api/booth-log row (admin-ui's useNowPlayingTasteAttribution: "no second,
            // now-playing-specific diagnostics fetch"); no independently-shaped now-playing pick
            // field could have drifted in alongside it.
            Assert.Equal(
                new HashSet<string> { nameof(BoothLogEntryDto), nameof(BoothLogPickDto) },
                owningTypeNames);
        }
    }
}
