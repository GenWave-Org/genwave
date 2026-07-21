# Label contract

SPEC F78.3 / STORY-202. Every log line the `alloy` service ships to Loki carries **exactly**
these three indexed (stream) labels — nothing more. Dashboards and alerts (`observability/
grafana/`, when that lands) may query only these three; anything else about a line is content,
not an index key.

- `service` — the Docker Compose service name of the container the line came from (`api`,
  `engine`, `icecast`, `db`, `kokoro`, `piper`, `admin_ui`, `cloudflared`, `alloy`, ...). Set
  per-container by `observability/alloy/config.alloy`'s `discovery.relabel` rule, from Docker's
  `com.docker.compose.service` container label — no env var, it's intrinsic to which container
  emitted the line.
- `station` — this deployment's operator-chosen identity (which physical/logical station this
  is — every GenWave deployment is one station, SPEC's "one deployment == one radio station").
  Fed by the `ALLOY_STATION_LABEL` compose environment variable (`compose.yaml`, `alloy`
  service), default `genwave`.
- `env` — which environment this deployment is (`demo`, `homelab`, ...). Fed by the
  `ALLOY_ENV_LABEL` compose environment variable, default `dev`.

## Why only three

Loki's cost and query performance are governed by the *cardinality* of the indexed label set —
every distinct combination of label values opens a new stream. `service` is low-cardinality (one
value per compose service, a handful total) and the one label worth slicing dashboards by.
`station`/`env` are each a single constant value per deployment. Multiplied together that's a
small, bounded number of streams per station, regardless of log volume. Anything with unbounded
or high cardinality — container ID, timestamp, request ID, the log message itself — stays as line
content (grep/LogQL-filterable, not indexed) instead of becoming a label. This is enforced in the
pipeline itself: `loki.process`'s `stage.label_keep` in `config.alloy` drops any label besides
these three before a line is ever delivered, so a future discovery tweak (or an upstream Alloy
default) can't silently widen the indexed set.

Note: a Loki server with `discover_service_name`/`discover_log_levels` enabled will add its own
extra indexed labels (`service_name`, `detected_level`) on ingestion — that's Loki-side behavior
outside this repo's boundary (SPEC F78.11: the `genwave-infra` repo owns the concrete Loki stack
config). This repo's contract is about what `alloy` itself sets and ships.

## Everything else is line content

Anything not in the three labels above — log level, request/correlation IDs, exception text,
container ID, timestamps beyond the entry's own, free-form message text — is part of the log
*line*, never promoted to a label. Query it with LogQL line filters (`|= "..."`, `| json`, `|
logfmt`, ...) scoped by `service`/`station`/`env`, not by adding more labels.
