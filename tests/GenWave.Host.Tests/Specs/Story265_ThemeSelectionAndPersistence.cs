// STORY-265 — Selection and persistence (SPEC F102.5, F102.14, F102.15)
//
// BDD specification — xUnit. Precedence, highest first:
//     visitor cookie → Station:Theme settings row → Station:Theme env default → shipped default
// and an unresolvable slug falls back at EVERY level rather than erroring.
//
// Station:Theme is an allowlisted LIVE setting presented as a closed CHOICE of shipped
// slugs. That needs a new SettingKind.Choice — today's kinds are Boolean/Number/NumberList/
// String only, and String would render a free-text box where a typo silently yields an
// unresolvable slug. A UI special-case was rejected: it breaks the allowlist's
// self-describing contract.
//
// ⚠️ Selection inherits the Station:SpectatorMode trap — a saved settings row silently
// outranks the env value forever. That is how a pinned demo box's theme "won't change".
//
// PENDING T163–T165.

using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureThemeSelectionAndPersistence
{
    const string PendingChoiceKind = "Pending T163 — see docs/PLAN.md";
    const string PendingResolution = "Pending T164 — see docs/PLAN.md";
    const string PendingWire = "Pending T165 — see docs/PLAN.md";

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioTheVisitorCookieOutranksTheStationSetting
    {
        [Fact(Skip = PendingResolution)]
        public void TheCookiesThemeIsResolved()
        {
            // Arrange: a station setting naming one theme, a visitor cookie naming another.
            // Act:     resolve the theme for that visitor.
            // Assert:  the cookie's theme wins (AC1).
            Assert.Fail("pending T164 — cookie precedence");
        }
    }

    public sealed class ScenarioTheSettingsRowOutranksTheEnvDefault
    {
        [Fact(Skip = PendingResolution)]
        public void TheSettingsRowsThemeIsResolved()
        {
            // Arrange: an env default naming one theme, a saved settings row naming another.
            // Act:     resolve with no visitor cookie.
            // Assert:  the settings row wins (AC2).
            Assert.Fail("pending T164 — settings-row precedence");
        }
    }

    public sealed class ScenarioTheEnvDefaultOutranksTheShippedDefault
    {
        [Fact(Skip = PendingResolution)]
        public void TheEnvDefaultsThemeIsResolved()
        {
            // Arrange: an env default naming a theme, no saved settings row.
            // Act:     resolve with no visitor cookie.
            // Assert:  the env default wins (AC3). This is how a pinned demo box gets its look.
            Assert.Fail("pending T164 — env precedence");
        }
    }

    public sealed class ScenarioTheShippedDefaultIsTheFloor
    {
        [Fact(Skip = PendingResolution)]
        public void TheShippedDefaultThemeIsResolved()
        {
            // Arrange: no cookie, no settings row, no env default.
            // Act:     resolve the theme.
            // Assert:  the shipped default is resolved (AC4).
            Assert.Fail("pending T164 — shipped-default floor");
        }
    }

    public sealed class ScenarioTheSettingIsLive
    {
        [Fact(Skip = PendingWire)]
        public void TheNextRequestServesTheNewThemeWithNoRestart()
        {
            // Arrange: a running api.
            // Act:     change Station:Theme via PUT /api/settings.
            // Assert:  the very NEXT request serves the new theme, with no api restart
            //          (AC5) — the SettingApplyMode.Live contract, same shape as
            //          Station:PublicStreamUrl.
            Assert.Fail("pending T165 — live setting wire acceptance");
        }
    }

    public sealed class ScenarioTheSettingPresentsAsAClosedChoice
    {
        [Fact(Skip = PendingChoiceKind)]
        public void ItsKindIsAChoiceOverTheShippedSlugs()
        {
            // Arrange: the settings surface describing Station:Theme.
            // Act:     read its kind.
            // Assert:  it is a choice over the shipped slugs, NOT free text (AC6). Requires
            //          SettingKind.Choice — Boolean/Number/NumberList/String are today's set.
            Assert.Fail("pending T163 — SettingKind.Choice");
        }
    }

    public sealed class ScenarioSelectionWorksOnAnUnadministrableBox
    {
        [Fact(Skip = PendingWire)]
        public void TheEnvSeededThemeIsServedWithNoAdminSurfaceReachable()
        {
            // Arrange: a station running with Admin:Enabled=false and an env-seeded
            //          Station:Theme (the compose.demo.yaml Station__* pattern).
            // Act:     serve the spectator page.
            // Assert:  it renders the env-seeded theme, with no admin surface reachable
            //          (AC7). The demo box pins its look this way.
            Assert.Fail("pending T165 — appliance-mode wire acceptance");
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioASavedRowSilentlyOutranksTheEnvValue
    {
        [Fact(Skip = PendingResolution)]
        public void TheSavedRowWinsAndItsSourceIsReportable()
        {
            // Arrange: an env-seeded Station:Theme plus a settings row saved earlier naming
            //          a different theme.
            // Act:     resolve the theme.
            // Assert:  the saved row wins, AND the setting's reported source makes that
            //          diagnosable (AC8). Without the source, this is indistinguishable from
            //          "the env var didn't take" — the Station:SpectatorMode gotcha class,
            //          which DEPLOYMENT.md documents.
            Assert.Fail("pending T164 — env-vs-row diagnosability");
        }
    }

    public sealed class ScenarioRejectingUnresolvableSlugs
    {
        [Fact(Skip = PendingResolution)]
        public void AnUnknownSlugInTheSettingFallsBackToTheShippedDefault()
        {
            // Arrange: Station:Theme naming a slug no shipped theme matches.
            // Act:     resolve the theme.
            // Assert:  the shipped default is resolved rather than an error (AC9).
            Assert.Fail("pending T164 — setting slug fallback");
        }

        [Fact(Skip = PendingResolution)]
        public void AnUnknownSlugInTheCookieFallsBackToTheStationTheme()
        {
            // Arrange: a visitor cookie naming a slug no shipped theme matches.
            // Act:     resolve the theme for that visitor.
            // Assert:  the STATION's theme is resolved (AC10) — not the shipped default, and
            //          not an error. A stale cookie from a removed theme must not strand a
            //          visitor away from what the station chose.
            Assert.Fail("pending T164 — cookie slug fallback");
        }
    }
}
