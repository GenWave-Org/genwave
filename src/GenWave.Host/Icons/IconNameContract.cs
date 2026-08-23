namespace GenWave.Host.Icons;

/// <summary>
/// The icon-name contract (SPEC F130.2, STORY-337, PLAN T302) — the ONE set of icon names an
/// installed pack's <c>icons</c> map may usefully cover, derived from the house
/// <c>admin-ui/app/(authed)/_components/icons.tsx</c> export set: each <c>XxxIcon</c> export becomes
/// its kebab-case name, minus the trailing "Icon" (e.g. <c>PersonaCatalogIcon</c> →
/// <c>persona-catalog</c>) — the exact set of icon SLOTS the admin chrome actually has today. A name
/// outside this set is not wrong, merely inert (SPEC F130.2 — "a pack may cover any subset; names
/// outside the contract are ignored with one install-time WARN"); <see cref="IconPackDefinitionParser"/>
/// reports every ignored name via <see cref="IconPackValidationResult.Valid.IgnoredNames"/> so PLAN
/// T303's install route can log that WARN once per name.
///
/// <para>
/// PARITY (PLAN T68's own golden-table idiom, applied here): <c>Story337_IconPacksSwapTheChrome.cs</c>'s
/// own <c>TheIconNameContractMatchesTheHouseIconExports</c> fact string-parses <c>icons.tsx</c>
/// directly (no TS toolchain runs inside xUnit — the same "repo-content-fact" trick
/// <c>FeatureSettingsHelpKeysParity</c> (tests/GenWave.Host.Tests/Specs/Story151_SeededDefaults.cs)
/// already established) and asserts this exact set against every derived name — a change to EITHER
/// side that drifts from the other fails that fact, never a silent one-sided drift. Unlike the
/// Slugify parity guard (PLAN T68, <c>Story192_PersonaSlugParity.cs</c>), there is no separately
/// authored TS case table to keep in step: <c>icons.tsx</c> IS the one source SPEC F130.2 names; this
/// constant is the only OTHER place the set is written down, and only in the app (the catalog schema
/// docs mirror is PLAN T309's job, not this one's).
/// </para>
/// </summary>
public static class IconNameContract
{
    /// <summary>Every icon slot the admin chrome renders today — the kebab-cased, "Icon"-suffix-
    /// stripped form of each <c>icons.tsx</c> export, in that file's own declaration order.</summary>
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.Ordinal)
    {
        "dashboard",
        "live",
        "announcements",
        "catalog",
        "safe-content",
        "health",
        "persona",
        "persona-catalog",
        "booth-log",
        "settings",
        "sign-out",
        "sun",
        "moon",
        "menu",
        "close",
        "vote-up",
        "vote-down",
        "restore",
        "taste-thumb-up",
        "taste-thumb-down",
        "schedule",
        "shows",
        "wardrobe",
        "editor",
        "exploration",
    };
}
