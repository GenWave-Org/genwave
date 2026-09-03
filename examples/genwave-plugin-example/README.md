# GenWave plugin example — dice roll

A minimal, complete `IContextProvider` plugin against the GenWave plugin SPI (SPEC F156/F157). No
HTTP, no external dependency, no config beyond one optional knob — small enough to read end to end
in a couple of minutes, real enough that `GenWave.Plugins.PluginLoader` actually loads it.

- `DiceRollPlugin.cs` — the `IGenWavePlugin` entry point. `Register` adds one context provider and
  nothing else.
- `DiceRollContextProvider.cs` — the `IContextProvider`. Rolls a die, hands back one fact, reads an
  optional `Plugins:{slug}:Sides` setting with a default.
- `plugin.json` — the manifest the loader discovers this plugin by.

## Quickstart — writing your own plugin

1. Create a class library:

   ```
   dotnet new classlib -n MyStationPlugin -f net10.0
   ```

2. Reference the published contract package:

   ```
   dotnet add package GenWave.Abstractions --version 5.6.0
   ```

   (This example's own `.csproj` uses a `ProjectReference` to the in-repo project instead — see
   that file's own comment for why. Your plugin uses the `PackageReference` form above.)

3. Implement `IGenWavePlugin`:

   ```csharp
   public sealed class MyPlugin : IGenWavePlugin
   {
       public string Name => "My Plugin";

       public void Register(IPluginHost host) =>
           host.AddContextProvider(new MyContextProvider());
   }
   ```

   `Register` must be inert — construct and hand over your implementations, nothing more. No I/O, no
   threads, no blocking work; the loader calls it synchronously, once, before the host finishes
   booting. Real work (an HTTP call, a file read) belongs inside your `IContextProvider`/
   `IAdSpotSource` implementation, run lazily once the station is actually airing.

   Every `IContextProvider` you register also needs a `Key`. It must match `^[a-z0-9-]+\z` (lowercase
   ASCII letters, digits, hyphens — nothing else; `\z` rather than `$` — `$` matches before a
   trailing newline in .NET regex, `\z` is the airtight end-of-string anchor, the house's own
   convention) and be unique against both the house's own built-in providers (`weather`, `history`)
   and every OTHER plugin's provider keys — including a SECOND provider registered by this SAME
   plugin: two `AddContextProvider` calls sharing one key collide too, not just a cross-plugin match.
   A malformed or colliding key does not just drop the offending provider(s) — it skips the **whole
   plugin**, the same WARN+skip posture as any other load failure (SPEC F156.4/F156.6). This
   example's own key is `"example-dice"` — see `DiceRollContextProvider.Key`.

4. Write `plugin.json` beside your built assembly:

   ```json
   {
     "name": "My Plugin",
     "version": "1.0.0",
     "assembly": "MyStationPlugin.dll",
     "entryType": "MyNamespace.MyPlugin",
     "abstractions": "5.6.0"
   }
   ```

   | Field | Meaning |
   | --- | --- |
   | `name` | Display name. Untrusted, third-party text — see the note below on which `name` the loader actually reports. |
   | `version` | Your own version string. Any non-blank value; nothing parses it. |
   | `assembly` | A bare on-disk file name — see "The `assembly` rule" below. |
   | `entryType` | Your `IGenWavePlugin` implementation's full name — see "The `entryType` rule" below. |
   | `abstractions` | The `GenWave.Abstractions` version you built against. Informational only. |

   The plugin's own identity on disk (its "slug") is the directory name you mount it under, never a
   field inside the manifest — see this example's own `plugin.json` for a filled-in reference.

   **Which `name` wins.** The manifest's own `name` field — not `IGenWavePlugin.Name` — is what the
   loader reports (`PluginLoadReport.Name`, the eventual source for `GET /api/status`'s `plugins[]`
   array, PLAN T394). `IGenWavePlugin.Name` is never read on this path today; keep both in sync by
   hand until/unless that changes.

   **The `assembly` rule.** This must be a bare, on-disk-safe file name the loader can resolve
   straight against the plugin's own directory — never a path. Concretely, rejected outright: any
   `/` or `\`, a `:` anywhere, a `..` substring, the bare string `.`, a NUL character, and **any
   whitespace at all — leading, trailing, or embedded**. That last one is the pitfall newcomers hit
   first:
   `dotnet new classlib -n "My Station Plugin"` (a name with spaces) produces `My Station
   Plugin.dll`, which this rule refuses outright — the whole plugin is skipped, not just renamed
   around. Pick a project name with no spaces, or override the built assembly's name explicitly:

   ```xml
   <PropertyGroup>
     <AssemblyName>MyStationPlugin</AssemblyName>
   </PropertyGroup>
   ```

   **The `entryType` rule.** Resolved via `Assembly.GetType(entryType)` — a real CLR type lookup, not
   a string match against a class name. Two things follow: the value must be the FULL,
   namespace-qualified name (`MyNamespace.MyPlugin`, never just `MyPlugin`), and a NESTED type uses
   `+` to separate the outer type from the inner one, never `.` (`MyNamespace.Outer+MyPlugin`). The
   resolved type must also implement `IGenWavePlugin` and expose a **public parameterless
   constructor** — the loader activates it with `Activator.CreateInstance(Type)`, which only ever
   calls that constructor; anything else (a required constructor argument, a private/internal one,
   none at all) skips the whole plugin as `EntryTypeNotConstructible`.

5. Build, then mount the output directory. `dotnet build` on this example's own project produces
   `bin/Debug/net10.0/`, containing `ExamplePlugin.dll` and `plugin.json` side by side — already a
   valid `{Plugins:Root}/<slug>/` payload, no packaging step. Your own project's build output works
   the same way once `plugin.json` is set to copy alongside it.

## Enabling a plugin (two independent knobs)

The loader itself (`GenWave.Plugins.PluginLoader`, PLAN T392) is real and this example loads through
it today (see this repo's own `GenWave.Plugins.Tests`). The station-level wiring — the two-knob gate
in `Program.cs`, the INFO line naming a missing knob, `plugins[]` on `GET /api/status`, the dashboard
tile, and the booth-log narrative rows (PLAN T394) — is live. Both of these must be true, or the door
stays closed:

1. `Plugins:Enabled=true` — env/compose-only, never on the live settings allowlist.
2. `Plugins:Root` mounted to a directory containing one subdirectory per plugin slug, each holding
   that plugin's `plugin.json` + assembly. The `compose.plugins.yaml` overlay mounts this
   (read-only) at `Plugins:Root`'s own default — see [DEPLOYMENT.md](../../DEPLOYMENT.md)'s Plugins
   section for the full compose recipe.

There is no live toggle: the plugin set loads once, at boot. Changing it is a restart.

## Optional settings

A plugin reads its own configuration through `IPluginHost.Setting(key)`, which resolves
`Plugins:{slug}:{key}` from the host's configuration — env/compose-only in v1, no live-reload path.
This example reads `Plugins:{slug}:Sides`, falling back to a 6-sided die when the value is unset,
unparsable, or out of the accepted 2–1000 range (an unreasonably large value would otherwise crash
the roll itself — see `DiceRollContextProvider.ResolveSides` for the full rule and why the upper
bound exists).

## Licensing

Compiling against `GenWave.Abstractions` is MIT-clean for anyone. A plugin loaded in-process runs
inside GenWave's own AGPL process, so a **distributed** plugin binary needs an AGPL-compatible
license; a private, undistributed plugin (the personal-station case) carries no obligation. See
[`/PLUGINS.md`](../../PLUGINS.md) (SPEC F157.3) for the full posture.
