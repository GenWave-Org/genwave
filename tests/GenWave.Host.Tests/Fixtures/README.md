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

## `mixed-catalog-index.json` — the shelf's kind-routed fake index (PLAN T185, STORY-273)

A fake `index.json`, shaped exactly per `genwave-catalog`'s `schemas/index.schema.json` and
`tools/build_index.py`'s own emitted shape: one persona entry (`bestFor`, the legacy `card` file-ref,
no `kind` key — the pre-F103.2 shape T176 must keep parsing) and one `kind:"theme"` entry (the
`manifest` file ref, plus the optional `preview` object T185 admits). The theme entry's `slug` and
`preview` swatch values are `golden-frequency`'s own light/dark `bg`/`surface`/`ink`/`accent`/
`accent-2` tokens (see `golden.theme.json` above) — realistic values, not placeholders, while the
`manifest`/`meta` `sha256` fields stay placeholder-shaped (64 hex chars): the index ROUTE this
fixture drives (`GET /api/catalog/index`) never fetches an entry's manifest/meta bytes to build the
shelf listing, so nothing here needs to hash-verify against real file content.

`Specs/Story273_ThemeShelfPreview.cs` drives this file through the real `GET /api/catalog/index`
route and separately through `CatalogIndexValidator.TryValidate` directly, proving the shelf lists
both kinds and the theme entry's preview swatches reach the wire — never regenerate this file to
add a THIRD entry/kind without updating that spec's own entry-count assertions.
