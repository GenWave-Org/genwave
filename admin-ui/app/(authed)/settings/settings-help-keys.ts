/**
 * Mirrors `GenWave.Host.Configuration.StationSettingsAllowlist.All`'s key list, in the same
 * order (SPEC F55.3, closes gitea-#230/gitea-#231).
 *
 * Jest cannot read the C# allowlist directly, so this small TS module is the pragmatic parity
 * anchor between the two toolchains:
 *   - `settings-help-coverage.spec.tsx` imports {@link SETTINGS_HELP_KEYS} to build its synthetic
 *     settings fixture and asserts every one of these keys renders help text.
 *   - An xUnit fact (`FeatureSettingsHelpKeysParity` in
 *     `tests/GenWave.Host.Tests/Specs/Story151_SeededDefaults.cs`) string-parses this very
 *     file and asserts its key list is equal, in order, to `StationSettingsAllowlist.All`.
 *
 * A key added to only one side therefore fails a spec on BOTH the C# and TS toolchains — never a
 * silent drift. Keep this list's content and order in exact 1:1 sync with the allowlist; do not
 * derive it dynamically (the whole point is two independently-authored sources a guard can catch
 * diverging).
 */
export const SETTINGS_HELP_KEYS = [
  "Loudness:TargetLufs",
  "Loudness:CeilingDbtp",
  "Station:Name",
  "Station:Voice",
  "Station:Cadence:LeadInBeforeEachTrack",
  "Station:Cadence:BackAnnounceAfterEachTrack",
  "Station:Cadence:StationIdEveryNUnits",
  "Station:Scope:LibraryIds",
  "Station:SafeScope:LibraryIds",
  "Station:Persona:ActiveId",
  "Station:Rotation:RecentWindow",
  "Station:Rotation:ArtistSeparation",
  "Tts:Endpoint",
  "Llm:Endpoint",
  "Llm:Model",
  "Llm:TimeoutSeconds",
  "Tts:RenderBudgetSeconds",
  "Tts:BlurbRetentionHours",
  "Llm:MaxCopyChars",
  "Admin:PlayHistoryCapacity",
  "Library:ScanIntervalSeconds",
  "Library:EnrichmentConcurrency",
  "Library:Scan:MissThreshold",
  "Library:YearLookup:Enabled",
  "Library:YearLookup:Endpoint",
  "Library:YearLookup:MinScore",
  "GW_XFADE_MIN",
  "GW_XFADE_MAX",
  "GW_SAFE_GAP_SECONDS",
  "Library:CueDetection:MinSilenceDurationSec",
  "Library:Energy:WindowSeconds",
] as const;

export type SettingsHelpKey = (typeof SETTINGS_HELP_KEYS)[number];
