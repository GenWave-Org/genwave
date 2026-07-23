# 🖥️ Hardware Compatibility

What GenWave actually runs on, what each service needs, and how confident we are in every claim.
This file is the source of truth for hardware guidance (gh-#20) — **contribute your own box via
PR**: add a row to the deployments table with your specs and what worked (or didn't).

## 🎨 Confidence legend

| Mark | Meaning |
|---|---|
| 🟢 | **Verified** — GenWave has demonstrably run here, or the number was measured/observed live |
| 🟡 | **Expected** — derived from configured limits or design targets; not independently measured |
| 🔴 | **Unverified / known-problematic** — no test has been run, or a problem was observed |

## 📦 Known deployments

| Machine | CPU / arch | RAM | Role | Status | Notes |
|---|---|---|---|---|---|
| `demo.genwaveradio.com` appliance | *(unrecorded)* x86-64 | *(unrecorded)* | Public demo station, full stack + LLM + tunnel + logging | 🟢 | Runs the pinned release 24/7 (health-probed by CI). Source of the one live-observed sizing fact: ollama at a 3 GB fence OOM-killed constantly; stable at **1 CPU / 6GB** (observed 2026-07-21, v2.2.0 rollout) |
| Development machines | x86-64 | *(varies)* | `./launch.sh` dev flow, full stack from source | 🟢 | Linux + Docker; no specs recorded |

*Have GenWave running somewhere else — a NUC, an old laptop, a VPS? Add it here.*

## 🎯 Design target

GenWave's stated hardware goal ([docs/PROJECT.md](docs/PROJECT.md)):

> **a modest CPU-only box runs the whole station; features that require a GPU don't ship in year one.**

- 🟢 **CPU-only by design** — TTS uses the CPU builds (`kokoro-fastapi-cpu`, Piper ONNX); the demo
  LLM (`llama3.2:3b` via ollama) runs CPU inference on one fenced core. No GPU is used anywhere.
- 🟡 **x86-64 only in practice** — the published GHCR images are built `amd64`-only. Nothing in the
  stack is *known* to be arch-specific, but:
- 🔴 **ARM64 / Raspberry Pi: untested** — no ARM images are published and no ARM run has ever been
  recorded. Building locally on ARM64 may work; nobody has claimed it does.

## 🧩 What each service needs

Configured limits come from `compose.yaml` / `compose.demo.yaml`; "real footprint" values are the
notes recorded alongside them.

| Service | Configured limit | Real footprint | Confidence | Notes |
|---|---|---|---|---|
| `kokoro` (TTS) | 3 GB cap | ~1.2 GiB fresh baseline | 🟢 footprint / 🟡 cap | Cap is a fail-closed backstop, not a requirement |
| `ollama` (DJ brain, demo profile) | **1 CPU / 6GB fence** | needs > 3 GB with `llama3.2:3b` resident (`KEEP_ALIVE=-1`) | 🟢 | Live-observed: 3 GB fence = constant OOM kills. Cold model load ~25 s+; a full persona prompt on one fenced core takes ~25–30 s even warm — set `Llm:TimeoutSeconds: 60`. Size the model to the fence |
| `piper` (fallback TTS) | 768 MB cap | well under 1 GiB with a "medium" voice | 🟢 footprint / 🟡 cap | ONNX runtime + `en_US-lessac-medium`, downloaded on first boot |
| `cloudflared` (tunnel profile) | 128 MB cap | ~20–30 MiB idle | 🟢 | |
| `alloy` (logging profile) | 256 MB cap | — | 🟡 | Single-daemon log-tailing sidecar |
| `db` / `icecast` / `engine` / `api` / `admin_ui` | *(uncapped)* | modest | 🟡 | No limits configured; none has ever been the memory pressure point |

## 📐 Sizing guidance (derived, not measured)

These totals are **derived** from the numbers above — nobody has bisected a real minimum:

- 🟡 **Without the LLM** (music + TTS patter, no DJ brain): ~**4 GB** RAM should be comfortable —
  kokoro's ~1.2 GiB baseline is the biggest resident, everything else is small.
- 🟡 **With the LLM resident** (the demo shape): **8 GB minimum, 16 GB comfortable** — the 6 GB
  ollama fence plus kokoro's baseline already crowds an 8 GB box.
- 🟡 **CPU**: the demo fences ollama to a single core and still renders patter within a 120 s
  budget; any modern multi-core x86-64 CPU should do. More cores mainly help enrichment
  (ffmpeg analysis of your library) finish sooner.
- 🟢 **Disk**: your music library (bind-mounted **read-only**) plus modest named volumes
  (Postgres data, rendered TTS segments, Piper models). Size to the library.

## ✅ Software requirements

| Requirement | Value | Confidence |
|---|---|---|
| OS | Linux with Docker Engine (the only deployment shape ever run) | 🟢 |
| Docker Compose | **v2.24+** (the demo overlay uses the `!override` merge tag) | 🟢 |
| GPU | none — not used anywhere | 🟢 |

## 🤝 Contributing an entry

1. Run the stack (`./launch.sh`, or the `--pinned` appliance flow — see [DEPLOYMENT.md](DEPLOYMENT.md)).
2. Note CPU model, core count, RAM, storage, and which profiles you ran (`admin`, `logging`,
   `tunnel`, the demo LLM overlay).
3. PR a row into **Known deployments** with 🟢 for what you verified and a note for anything that
   needed tuning (e.g. a different ollama fence). Problems are as valuable as successes — file
   them as issues and reference them from a 🔴 row.
