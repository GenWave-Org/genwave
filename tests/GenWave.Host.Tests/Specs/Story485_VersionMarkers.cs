// STORY-485 — The markers are reported (gh-#9 + gh-#868 · SPEC F211.5–F211.6 · PLAN T593, T594)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.
// Entry point: GET /api/status + GET /internal/engine-config through WebApplicationFactory with IAppVersion
// from "5.14.0+abc1234" and a fake ISchemaJournal; boot WARNs read from a capturing logger provider.

namespace GenWave.Host.Tests.Specs;

public static class FeatureVersionMarkers
{
    const string PendingStatus = "pending: T593 — status version block + drift WARN (STORY-485)";
    const string PendingEngine = "pending: T594 — engine-config GW_APP_VERSION (STORY-485)";

    public sealed class ScenarioStatusOnAMatchedStation
    {
        // Given: IAppVersion "5.14.0+abc1234"; Expected 49; fake journal Applied 49; GET /api/status

        /// <summary>AC1 — version.app is "v5.14.0"</summary>
        [Fact(Skip = PendingStatus)]
        public void App() => Assert.Fail(PendingStatus);

        /// <summary>AC2 — version.build is "5.14.0+abc1234"</summary>
        [Fact(Skip = PendingStatus)]
        public void Build() => Assert.Fail(PendingStatus);

        /// <summary>AC3 — version.schema.expected is 49</summary>
        [Fact(Skip = PendingStatus)]
        public void SchemaExpected() => Assert.Fail(PendingStatus);

        /// <summary>AC3 — version.schema.applied is 49</summary>
        [Fact(Skip = PendingStatus)]
        public void SchemaApplied() => Assert.Fail(PendingStatus);

        /// <summary>AC6 — no schema WARN at boot</summary>
        [Fact(Skip = PendingStatus)]
        public void NoWarn() => Assert.Fail(PendingStatus);
    }

    public sealed class ScenarioEngineConfig
    {
        // Given: IAppVersion "5.14.0+abc1234"; GET /internal/engine-config

        /// <summary>AC4 — exactly four keys</summary>
        [Fact(Skip = PendingEngine)]
        public void FourKeys() => Assert.Fail(PendingEngine);

        /// <summary>AC4 — the last line is GW_APP_VERSION=v5.14.0</summary>
        [Fact(Skip = PendingEngine)]
        public void CarriesTheVersion() => Assert.Fail(PendingEngine);
    }

    public sealed class ScenarioTheEngineEntrypoint
    {
        // Given: engine/entrypoint.sh run against a stub engine-config serving GW_APP_VERSION=v5.14.0,
        //        liquidsoap stubbed on PATH

        /// <summary>AC5 — one boot line names v5.14.0</summary>
        [Fact(Skip = PendingEngine)]
        public void LogsTheVersion() => Assert.Fail(PendingEngine);

        /// <summary>AC5 — the script references GW_APP_VERSION only in that log line</summary>
        [Fact(Skip = PendingEngine)]
        public void UsesItForNothingElse() => Assert.Fail(PendingEngine);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioSchemaBehind
    {
        // Given: Expected 49; fake journal Applied 47; api boot; GET /api/status

        /// <summary>AC7 — exactly one schema WARN</summary>
        [Fact(Skip = PendingStatus)]
        public void OneWarn() => Assert.Fail(PendingStatus);

        /// <summary>AC7 — the WARN names 47, 49 and ./migrate.sh</summary>
        [Fact(Skip = PendingStatus)]
        public void WarnNamesBothAndTheFix() => Assert.Fail(PendingStatus);

        /// <summary>AC9 — status still returns 200 (drift never blocks)</summary>
        [Fact(Skip = PendingStatus)]
        public void StatusServes() => Assert.Fail(PendingStatus);
    }

    public sealed class ScenarioEmptyJournal
    {
        // Given: Expected 49; fake journal Applied null; api boot; GET /api/status

        /// <summary>AC8 — exactly one WARN naming 49, "none" and ./migrate.sh</summary>
        [Fact(Skip = PendingStatus)]
        public void WarnNamesNone() => Assert.Fail(PendingStatus);

        /// <summary>AC8 — version.schema.applied is null</summary>
        [Fact(Skip = PendingStatus)]
        public void AppliedIsNull() => Assert.Fail(PendingStatus);
    }

    public sealed class ScenarioJournalThrows
    {
        // Given: fake journal GetAppliedAsync throws; api boot; GET /api/status

        /// <summary>AC10 — status returns 200</summary>
        [Fact(Skip = PendingStatus)]
        public void StatusServes() => Assert.Fail(PendingStatus);

        /// <summary>AC10 — version.schema.applied is null</summary>
        [Fact(Skip = PendingStatus)]
        public void AppliedIsNull() => Assert.Fail(PendingStatus);
    }
}
