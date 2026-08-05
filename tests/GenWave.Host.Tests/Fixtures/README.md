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

## `golden.font.json` — the `.font.json` FORMAT contract, not a byte-parity promise (PLAN T193, STORY-279 AC3)

The same authored-here-first pattern as `golden.theme.json` above, applied to the font kind (SPEC
F104.1/F104.2): a real, complete `CatalogFontManifest` for **Space Grotesk** (OFL-1.1, one upright
face) — family/files/licence/provenance fields shaped per `FONTS.md`'s own provenance-record
convention. Unlike `golden.theme.json`, this is deliberately NOT staged for `genwave-catalog` to
commit byte-for-byte identical: it pins the SHAPE `CatalogFontManifestSerializer` round-trips (field
names, nesting, the `null`-vs-string `version` posture), not the actual Space Grotesk pack's real
content — `"subset": "text"` (note-level review finding) is honest about what `golden-font.woff2`
below actually is (a `--text="GenWave 0123456789"` cut, not a real latin subset), and `"version":
"2.000"` is the real upstream TTF's own name ID 5 (`fonttools` `ttx`/`fonttools varLib.instancer`
convention, the JBM/Grenze vendoring precedent this app's own `FONTS.md` already follows for its
bundled fonts). T197's real Space Grotesk catalog pack will be a TRUE latin subset with its own,
different hash and `"subset": "latin"` — this fixture never claims to be that pack early.

`Specs/Story279_FontKindAssets.cs`'s `ScenarioGoldenParityFixtures.GoldenFontJsonRoundTripsByteStable`
proves this file round-trips byte-exactly through `CatalogFontManifestSerializer` — a hand-edit
that breaks either the shape or the serializer's naming policy goes red here.

**Regenerate ONLY by hand-authoring a new complete, valid manifest** and re-running the round-trip
fact until it's green — never partially edit an existing field.

## `golden-font.woff2` — the first BINARY golden fixture (PLAN T193, STORY-279 AC3)

A real, tiny woff2 face — Space Grotesk's own upstream variable TTF
(`github.com/floriankarsten/space-grotesk`), **text-subsetted** to `--text="GenWave 0123456789"`
(17 codepoints — deliberately NOT a latin subset) with `fonttools`' `pyftsubset` so the committed
fixture stays small (7,844 bytes). The T177 parity precedent applied to binary content for the
first time: this app has no runtime consumer for the bytes yet (that is T194's fetch/verify
transport), so the fixture's whole job today is being a real, valid woff2 with a known sha256 —
`4f8000489733987cfe711fb469bd932a3024290bea8bc44151f6807f588932ee` — for transport/parity specs in
BOTH repos. It is a FORMAT/transport fixture only: T197's real Space Grotesk pack will be a true
latin subset with its own, different bytes and hash (see the `golden.font.json` section above).

`Specs/Story279_FontKindAssets.cs`'s `ScenarioGoldenParityFixtures.TheGoldenWoff2FixtureHashesToItsRecordedSha256`
pins that hash directly; `Fixtures/font-catalog-index.json` below reuses the SAME real hash+byte
count in its `assets[]` entry, so a hand-edit that silently swaps this file's bytes goes red in two
places, not one.

**Regenerate ONLY via the recipe above** (a fresh `pyftsubset` invocation against the same upstream
TTF and text set) and update every hash this file's own remarks above name.

## `font-catalog-index.json` — a font entry with real asset hashes (PLAN T193, STORY-279 AC4)

A sibling to `mixed-catalog-index.json` (never that file itself — see its own remarks on why a third
entry/kind never lands there without updating Story273's entry-count assertions): one persona entry
(`valid-dj`, placeholder-shaped hashes, exactly `mixed-catalog-index.json`'s own persona entry) and
one `kind:"font"` entry (`space-grotesk`) whose manifest/meta stay placeholder-shaped (this fixture's
own route never fetches manifest/meta content either) but whose single `assets[]` entry carries
`golden-font.woff2`'s REAL sha256 and byte count.

`Specs/Story279_FontKindAssets.cs`'s `ScenarioOlderAppsSkipFontEntries.AnIndexCarryingAFontEntryStillServesEveryOtherEntry`
drives this file through `CatalogIndexValidator.TryValidate` directly, proving a `kind:"font"` entry
is now admitted (not forward-compat-skipped) alongside every other entry on the shelf.
