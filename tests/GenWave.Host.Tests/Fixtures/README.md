# Fixtures

## `golden.persona.json` — cross-repo parity pin (PLAN T107, STORY-231 AC2)

Byte-for-byte identical to `genwave-catalog`'s own `fixtures/golden.persona.json`, pinned at
`genwave-catalog@6c7faff56e3292a77cbb1b563fa87d7056f41140`. Both repos ship the SAME artifact so a
drift in either the app's `PersonaCardSerializer` or the catalog's card schema shows up as exactly
one deterministic red test — no cross-repo network call involved.

`Specs/Story231_GoldenCardParity.cs` proves this file imports, unmodified, through the real F79
import endpoint (`POST /api/personas/{slug}/import`) and round-trips byte-exactly through
`PersonaCardSerializer` — a hand-edit that breaks either property goes red here.

**Regenerate ONLY by copying `fixtures/golden.persona.json` from `genwave-catalog` verbatim** — run
from the parent directory holding both repo checkouts as siblings (`genwave/` and
`genwave-catalog/`), both path halves repo-root-relative:

```
cp genwave-catalog/fixtures/golden.persona.json genwave/tests/GenWave.Host.Tests/Fixtures/golden.persona.json
```

and update the pinned commit above. Never hand-edit this file in place — see
`genwave-catalog/README.md`'s own "golden fixture" section for how that repo generates it from
this app's real serializer in the first place.

## `golden.theme.json` — the `.theme.json` format contract (PLAN T177, STORY-269 AC5)

The opposite direction from `golden.persona.json` above: authored HERE first (a real, complete
`ThemeManifest` — distinct slug `golden-frequency`, never a shipped theme's own slug), staged for
`genwave-catalog` to commit byte-for-byte identical in a later task (T178+) — the same
cross-repo parity pin, just flowing app → catalog instead of catalog → app, because the format
didn't have a catalog-side owner yet when this fixture was authored.

`Specs/Story269_CatalogKindSeam.cs`'s `ScenarioTheGoldenThemeFixtureRoundTrips` proves this file
parses through the real `ThemeCatalog.Load` path and round-trips byte-exactly through
`ThemeManifestSerializer` — a hand-edit that breaks either the parser or the serializer's naming
policy goes red here.

**Regenerate ONLY by hand-authoring a new complete, valid manifest** (there is no catalog-side
copy to pull from yet) and re-running the round-trip fact until it's green — never partially edit
an existing field, since the fixture's whole job is being one fully-authored, byte-stable document.
