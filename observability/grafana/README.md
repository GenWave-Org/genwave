# GenWave Grafana — dashboards as code

SPEC F78.9 / STORY-204. This directory is the **source of truth** for GenWave's Grafana
dashboards and their provisioning — this repo owns the contract, `genwave-infra` owns the
running home-lab Grafana (SPEC F78.11).

```
observability/grafana/
  dashboards/                       # dashboard JSON — "ops" board proves the pipe (T50)
    genwave-ops.json
  provisioning/
    datasources/loki.yaml           # Loki datasource, uid "loki"
    dashboards/genwave.yaml         # file provider, folder "GenWave", allowUiUpdates: false
```

## How `genwave-infra` consumes this

1. Mount (or copy) `observability/grafana/provisioning/` to Grafana's
   `/etc/grafana/provisioning/`.
2. Mount (or copy) `observability/grafana/dashboards/` to the path
   `provisioning/dashboards/genwave.yaml`'s `options.path` points at
   (`/etc/grafana/provisioning/genwave-dashboards` by default — adjust both together if
   your mount layout differs).
3. Override `provisioning/datasources/loki.yaml`'s `url` to the real Loki instance
   (`http://loki:3100` here is only the in-stack address for this repo's own compose
   project) — keep the `uid: loki` unchanged, every dashboard query is pinned to it.

Everything else — panel layout, queries, template variables — travels as-is.

## Dashboards are code

Changes land as PRs against this repo, never as click-state in a running Grafana
(`allowUiUpdates: false` enforces this at the provisioning level). Edit the JSON, open a
PR, let `genwave-infra` pick up the new file on its next sync/redeploy.

## Contract labels only

Every panel query selects only on `service`/`station`/`env` (`observability/LABELS.md`,
SPEC F78.3) — nothing else is indexed in Loki, so nothing else belongs in a stream
selector `{...}`. `tests/GenWave.Host.Tests/Specs/Story204_DashboardsAreCode.cs` enforces
this for every committed dashboard.
