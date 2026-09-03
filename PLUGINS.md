# 🔌 Plugins

GenWave loads third-party code in-process through one narrow door: a plugin can *add* a
context provider (`IContextProvider` — the weather/history seam) or an ad-spot source
(`IAdSpotSource` — a break's own spot picker), and nothing else. There is no replace, no
unload, no interception. This document is the licensing posture (SPEC F157.3) plus the
practical how-to; the reference implementation lives at
[`examples/genwave-plugin-example/`](examples/genwave-plugin-example/).

## 📜 The licensing posture

GenWave Home is [AGPL-3.0-only](LICENSE). A plugin runs inside that same process, so its
license obligations depend on what you do with the binary — not on the fact that you wrote
one at all:

- **Compiling against `GenWave.Abstractions` is MIT-clean for anyone.** The contract
  package — `IGenWavePlugin`, `IPluginHost`, `IContextProvider`, `IAdSpotSource` and the
  rest of the SDK surface — is [MIT-licensed](src/GenWave.Abstractions/LICENSE), the same
  one deliberate exception the [README](README.md#license) carries for every other module.
  Linking against it, open or commercial, costs you nothing.
- **A plugin binary you *distribute*, loaded in-process, must carry an AGPL-compatible
  license.** It runs inside GenWave's own AGPL process at that point, and AGPL's own terms
  follow the distributed whole.
- **A private, undistributed plugin carries no obligation.** The personal-station case —
  you write a plugin, you run it on your own box, you never hand the binary to anyone else
  — is not a distribution event. Nothing here asks you to publish source or relicense
  anything you keep to yourself.
- **Out-of-process integrations are unconstrained.** Anything your plugin (or GenWave
  itself) *speaks to over a URL* — the same posture `Tts:Endpoint`/`Llm:Endpoint` already
  ship under — never loads into this process and carries no obligation from it at all. If
  your integration can be a service GenWave calls instead of a DLL GenWave loads, that
  route has no licensing question to answer in the first place.

None of this is legal advice — if a specific distribution plan needs a real opinion, get
one. It's the maintainer's own reading of what "runs in-process" versus "spoken to over a
URL" actually means for AGPL's linking boundary, written down so it doesn't have to be
re-litigated per plugin.

## 🧩 What a plugin can add

v1's expected set is exactly two contracts, both from `GenWave.Abstractions`:

- **`IContextProvider`** — the same seam the built-in weather/history providers implement.
  Your plugin hands back facts the DJ can work into patter.
- **`IAdSpotSource`** — the same seam `LibraryAdSpotSource` (the house floor) implements.
  Your plugin can win a break with a real ad without replacing the built-in source; sources
  form a pipeline, first non-null answer wins, the house source registers last.

Nothing else is reachable. `IPluginHost` — the only object your plugin ever sees — offers
`AddContextProvider`, `AddAdSpotSource`, and `Setting(key)` to read your own config. There
is no hook to swap the loudness pipeline, the TTS engine, the rotation predicate, or
anything else the station does; growing that set later is a minor, additive `Abstractions`
bump, never a breaking one.

**A loaded plugin is full-trust, in-process code — the door is narrow, not a sandbox.** Its
own `AssemblyLoadContext` exists to unify type identity (so the `IContextProvider`/
`IAdSpotSource` instances it registers are the SAME types the host's DI container expects,
not a look-alike copy), never to restrict what the loaded assembly can do — it can touch the
filesystem, the network, or anything else this process can reach, `IPluginHost`'s narrow
surface notwithstanding. Vetting the plugin's own code (or the party that built it) before
you mount it is the real gate; the SPI is a convention a well-behaved plugin follows, not an
enforced boundary.

## 🔧 The manifest

The loader discovers plugins by walking `{Plugins:Root}/<slug>/plugin.json` one directory
level deep — no recursion, no scanning for stray DLLs. Each manifest names:

| Field | Meaning |
| --- | --- |
| `name` | Display name (untrusted third-party text — logged only after sanitization) |
| `version` | Your own version string; any non-blank value |
| `assembly` | A bare on-disk file name, no path separators, no traversal, no whitespace |
| `entryType` | Your `IGenWavePlugin` implementation's full name |
| `abstractions` | The `GenWave.Abstractions` version you built against — **required** (a blank value is rejected outright; the value itself is never checked against anything downstream, so it is informational only in the sense that no compatibility gate reads it — yet) |

The plugin's identity on disk — its slug — is the directory name it's mounted under, never
a field inside the manifest itself. See the example's own
[README](examples/genwave-plugin-example/README.md) for the full field-by-field rules (the
`assembly` and `entryType` gotchas especially) and a working `plugin.json` to copy from.

## 🚪 Enabling the door (two independent knobs)

Both of these must be true, or the door stays closed:

1. **`Plugins:Enabled=true`** — env/compose-only, never on the live settings allowlist.
   Default `false` (fail-closed).
2. **A mounted `Plugins:Root`** (default `/plugins`) containing one subdirectory per
   plugin slug. The `compose.plugins.yaml` overlay mounts `./plugins` read-only at that
   path:

   ```bash
   docker compose -f compose.yaml -f compose.plugins.yaml up
   ```

Either knob alone does nothing observable except one INFO line naming the missing half —
`Plugins:Enabled` set with nothing mounted, or a mount present with the flag unset, both
leave the door closed with no plugin loaded. There is no live toggle: the plugin set loads
once, at boot. Adding, removing, or updating a plugin is a restart.

`Plugins:Enabled` only parses `true`/`false` (case-insensitive) — a value that reaches the
container as a literal empty string (the list form `- Plugins__Enabled=`, or a map entry
interpolated from a shell/`.env` variable that IS defined but empty or undefined) **fails
the boot**, it does not close the door. A bare, **valueless** map entry (`Plugins__Enabled:` with
nothing after the colon and no same-named variable to pass through) is different: it never
reaches the container at all, so it reads as absent and defaults quietly to `false` — the
door just stays closed. And the root `.env` file itself never reaches a container
un-interpolated — set `Plugins__Enabled` in a service's own `environment:` block (a local
`compose.local.yaml` stacked last), not `.env`. See [DEPLOYMENT.md](DEPLOYMENT.md)'s
Plugins section for the full recipe and the exact failure mode.

## 🛡️ Whole-plugin skip, never down

Any load, instantiation, or registration failure — a missing manifest field, a corrupt
DLL, a manifest naming a type that doesn't implement `IGenWavePlugin`, a context provider
whose `Key` collides with a built-in or an earlier plugin's — skips that **whole plugin**,
never a partial load, and the station boots and airs exactly as if the plugin were absent.
One WARN names the plugin and the cause. A well-behaved plugin never takes the station
down; a broken one is simply not there.

## 👀 Where load outcomes surface

- **`GET /api/status`** carries a `plugins: [{ name, version, contracts, state, reason? }]`
  array — `state` is `"loaded"` or `"skipped"`, with `reason` present only on a skip. Empty
  when the door is closed.
- **The admin dashboard** shows a Plugins tile once at least one plugin has been discovered
  — loaded or skipped, either counts. An unopened door or a mount with zero valid plugins
  shows no tile at all, even with `Plugins:Enabled` set.
- **The booth log** gets one narrative row per plugin outcome at boot — loaded or skipped,
  with the skip reason.

## 🏗️ Writing your own

Start from the reference plugin: [`examples/genwave-plugin-example/`](examples/genwave-plugin-example/)
is a minimal, complete `IContextProvider` plugin the real loader loads, with a step-by-step
Quickstart covering the `dotnet new classlib` → `PackageReference GenWave.Abstractions` →
implement `IGenWavePlugin` → write `plugin.json` → build path, and the exact pitfalls the
manifest rules above catch (the `dotnet new -n "My Station Plugin"` space-in-assembly-name
trap chief among them). Read its README before you start; it stays in sync with the real
parser and loader because CI builds it as part of this repository.
