# Options pattern & configuration — in depth

Configuration read as loose strings (`config["Streaming:MountPoint"]`)
scatters key names, defers typos to runtime, and can't be validated.
The options pattern centralizes shape, validation, and reload behavior.

---

## §binding — The basic shape

One options class per concern, bound from a named section, validated at
startup:

```csharp
public sealed class StreamingOptions
{
    public const string SectionName = "Streaming";

    [Required]
    public string MountPoint { get; init; } = string.Empty;

    [Required, Url]
    public string IcecastUrl { get; init; } = string.Empty;

    [Range(32, 320)]
    public int BitrateKbps { get; init; } = 128;
}
```

```csharp
builder.Services
    .AddOptions<StreamingOptions>()
    .BindConfiguration(StreamingOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

`ValidateOnStart()` is the load-bearing line: a bad config kills the
boot with a clear message instead of surfacing mid-request hours later.
For cross-field rules, add `.Validate(o => o.Min < o.Max, "Min must be
below Max")` or implement `IValidateOptions<T>` (see
`templates/OptionsSetup.cs`).

---

## §consuming — IOptions vs IOptionsSnapshot vs IOptionsMonitor

| Interface | Lifetime | Sees changes? | Use |
|---|---|---|---|
| `IOptions<T>` | Singleton | No (frozen at first resolve) | Default; stable config |
| `IOptionsSnapshot<T>` | Scoped | Per request/scope | Request-time reload semantics |
| `IOptionsMonitor<T>` | Singleton | Yes, + `OnChange` callback | Hosted services reacting to config changes |

Rules:
- Inject the options interface, not `IConfiguration`.
- A `BackgroundService` that should honor config edits without restart
  uses `IOptionsMonitor<T>.CurrentValue` each cycle (cheap) — not a
  cached `.Value` from startup.
- Pass `options.Value` (the POCO) into domain classes; don't let
  `IOptions<>` leak below the composition layer.

---

## §layering — Configuration sources and precedence

Later sources override earlier ones. The standard stack for a
containerized service:

1. `appsettings.json` — shape and safe defaults, committed.
2. `appsettings.{Environment}.json` — per-environment non-secrets.
3. **Custom providers** (e.g. a database-backed configuration provider) —
   registered explicitly, position chosen deliberately.
4. Environment variables — deployment-specific values and **all
   secrets** (`Streaming__IcecastUrl` maps to `Streaming:IcecastUrl`).
5. Command-line args.

Database-backed configuration (a config table as a provider):
- Decide and *document* whether DB values override env or vice versa —
  surprise precedence is a classic dead-air incident (a stale DB row
  silently beating a corrected env var, or the reverse).
- Implement reload via the provider's change token if admins edit
  config at runtime; consumers needing live values use
  `IOptionsMonitor`.
- Secrets don't belong in the general config table (see `security-api`
  references/aspnetcore.md §secrets).

---

## §named — Named options

When the same shape applies to multiple instances (several stream
outputs, multiple TTS voices):

```csharp
builder.Services.AddOptions<OutputOptions>("icecast-main")
    .BindConfiguration("Outputs:IcecastMain").ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<OutputOptions>("http-direct")
    .BindConfiguration("Outputs:HttpDirect").ValidateDataAnnotations().ValidateOnStart();

// consume
var main = optionsMonitor.Get("icecast-main");
```

Prefer this over inventing parallel option classes that differ only by
section name.

---

## §antipatterns

- `IConfiguration` injected into business logic — config shape leaks
  everywhere, no validation, string-typed keys.
- `config.GetValue<string>("Key") ?? "default"` scattered defaults —
  the default belongs on the options property, once.
- Re-reading and re-parsing config per request "to be fresh" — that's
  what `IOptionsSnapshot`/`IOptionsMonitor` are for.
- An options class with 30 properties spanning four concerns — split
  per consumer (Interface Segregation applied to config).
- Mutable options consumed concurrently: keep options POCOs `init`-only;
  a hosted service reading `CurrentValue` gets a fresh immutable
  instance on change instead of seeing a half-written one.
