# 🔌 Plugins

## What a plugin is

A plugin is a .NET library that GenWave loads when it starts, inside GenWave's own process. It can add
a little to the station and nothing more. There is no replacing, no unloading and no
interception of anything the station does.

## What a plugin can add

Two things, both defined in `GenWave.Abstractions`:

- **`IContextProvider`**: a source of facts the DJ can work into patter, like the built-in weather and history providers.
- **`IAdSpotSource`**: a way to supply an ad for a break. Sources are asked in order and the first real answer wins. The built-in library source goes last, and it is the only one that skips paused or recently aired sponsors.

A plugin only ever sees `IPluginHost`. It offers `AddContextProvider`, `AddAdSpotSource` and `Setting(key)` for reading the plugin's own settings.

A loaded plugin is full-trust code. It can reach the filesystem and the network like the rest of the process, so look over its code, or whoever built it, before you mount it.

## The manifest

Each plugin lives in `<plugins folder>/<slug>/plugin.json`, one folder deep. The slug is the folder name, not a field.

| Field | Meaning |
| --- | --- |
| `name` | Display name |
| `version` | Your own version string; any non-blank value |
| `assembly` | A bare file name: no path separators, no traversal, no whitespace |
| `entryType` | The full name of your `IGenWavePlugin` class |
| `abstractions` | The `GenWave.Abstractions` version you built against. Required, but nothing checks it yet |

The example's [README](examples/genwave-plugin-example/README.md) has the full rules and a working `plugin.json`.

## Turning plugins on

Both of these are needed:

1. The setting `Plugins:Enabled=true`. It defaults to `false` and can only be set in the environment, not on the Settings page.
2. A mounted plugins folder (`Plugins:Root`, default `/plugins`) with one subfolder per plugin. The `compose.plugins.yaml` overlay mounts `./plugins` there, read-only:

   ```bash
   docker compose -f compose.yaml -f compose.plugins.yaml up
   ```

With only one of the two, GenWave logs one INFO line naming the missing half and loads nothing. Plugins load once at start, so adding, removing or updating one means a restart.

`Plugins:Enabled` must be `true` or `false`. An empty string reaching the container stops the boot. Set it in the `api` service's own `environment:` block (for example in a `compose.local.yaml` stacked last), because the root `.env` file never reaches a container that way. [DEPLOYMENT.md](DEPLOYMENT.md) has the full recipe.

## When a plugin fails

Any failure to load, create or register a plugin skips the **whole plugin**: a missing manifest field, a corrupt file, a type that is not an `IGenWavePlugin`, or a context provider whose `Key` clashes with another. The station keeps running as if it were not there, and one warning names the plugin and the cause.

## Where to see what loaded

- `GET /api/status` has a `plugins` list with `name`, `version`, `contracts`, `state` (`loaded` or `skipped`) and, for a skip, `reason`.
- The admin dashboard shows a Plugins tile once any plugin has been found, loaded or skipped.
- The booth log gets one row per plugin at start, with the reason for any skip.

## Writing one

Start from [`examples/genwave-plugin-example/`](examples/genwave-plugin-example/), a small working `IContextProvider` plugin. Its README walks through the steps and the common traps. CI builds it with the rest of this repository, so it stays in step with the real loader.

## The licensing posture

GenWave Home is [AGPL-3.0-only](LICENSE). A plugin runs inside that process, so your obligations depend on what you do with the binary.

- **Compiling against `GenWave.Abstractions` is MIT-clean for anyone.** The contract package is [MIT-licensed](src/GenWave.Abstractions/LICENSE), open or commercial.
- **A plugin binary you distribute, loaded in-process, must carry an AGPL-compatible license.** It runs inside GenWave's AGPL process.
- **A private, undistributed plugin carries no obligation.** Writing one for your own station is not distribution.
- **Out-of-process integrations are unconstrained.** Anything GenWave speaks to over a URL, as it does with `Tts:Endpoint` and `Llm:Endpoint`, is never loaded into the process.

This is the maintainer's reading, not legal advice.
