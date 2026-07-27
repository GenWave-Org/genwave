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
