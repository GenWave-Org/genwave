# 🖥️ Hardware Compatibility

What GenWave actually runs on, what each service needs, and the confidence level for every claim.
This file is the source of truth for hardware guidance (gh-#20) — **contribute your own box via
PR**: add a row to the deployments table with your specs and what worked (or didn't).

## 🎨 Confidence legend

| Mark | Meaning |
|:---:|---|
| 🟢 | **Verified** — GenWave has demonstrably run here, or the number was measured/observed live |
| 🟡 | **Expected** — derived from configured limits or design targets; not independently measured |
| 🔴 | **Unverified / known-problematic** — no test has been run, or a problem was observed |

## 📦 Known deployments

### Computer Systems
| Machine | Arch | Core/vCPU Count | RAM | Storage | Role | Status | Notes | Verifier |
|---|---|:---:|:---:|:---:|---|:---:|---|---|
| `demo.genwaveradio.com` appliance (CCX23) | x86-64 | 4 | 16GB | 80GB | Public demo station, full stack + admin + LLM + tunnel + logging | 🟢 | Runs the pinned release 24/7 (health-probed by CI). Source of the one live-observed sizing fact: ollama at a 3 GB fence OOM-killed constantly; stable at **1 CPU / 6GB** (observed 2026-07-21, v2.2.0 rollout) | GenWave |
| Development machine | x86-64 | 112 | 512GB | 4TB | `./launch.sh` dev flow, full stack from source | 🟢 | Ubuntu 25.04 + Docker on Dell Precision 7920 Tower| GenWave |

### Internet Radios
| Make | Model | Status | Notes | Verifier |
|---|---|:---:|---|---|
| Grace Digital | Mondo Elite Classic | 🟢 | Works great! | GenWave |

*Have GenWave running somewhere else — a NUC, an old laptop, a VPS? Add it here!*

## 🎯 Design target

GenWave's stated hardware goal ([docs/PROJECT.md](docs/PROJECT.md)):

> **A modest CPU-only box runs the whole station; features that require a GPU don't ship in year one (hopefully never).**

- 🟢 **CPU-only by design** — TTS uses the CPU builds (`kokoro-fastapi-cpu`, Piper ONNX); the demo
  LLM (`llama3.2:3b` via ollama) runs CPU inference on one fenced core. No GPU is used anywhere.
- 🟡 **x86-64 only in practice** — the published GHCR images are built `amd64`-only. Nothing in the
  stack is *known* to be arch-specific, but:
- 🔴 **ARM64 / Raspberry Pi: untested** — no ARM images are published and no ARM run has ever been
  recorded. Building locally on ARM64 may work; nobody has claimed it does. See the
  **Raspberry Pi** section below for the gh-#213 spike's full audit.

## 🧩 What each service needs

Configured limits come from `compose.yaml` / `compose.demo.yaml`; "real footprint" values are the
notes recorded alongside them.

| Service | Configured limit | Real footprint | Confidence | Notes |
|---|:---:|---|:---:|---|
| `kokoro` (TTS) | 4 GB cap | ~1.2 GiB fresh baseline; leaks toward the cap under render load (upstream `#262`), long renders spike ~+0.5 GiB | 🟢 | Cap is a fail-closed backstop. Live-observed: 3 GB cap = OOM-bounce every ~24–30 h on the demo box, down to ~90 min in heavy render windows (gh-#276); 4 GB buys spike headroom over the leaked baseline |
| `ollama` (DJ brain, demo profile) | **1 CPU / 6GB fence** | needs > 3 GB with `llama3.2:3b` resident (`KEEP_ALIVE=-1`) | 🟢 | Live-observed: 3 GB fence = constant OOM kills. Cold model load ~25 s+; a full persona prompt on one fenced core takes ~25–30 s even warm — set `Llm:TimeoutSeconds: 60`. Size the model to the fence |
| `piper` (fallback TTS) | 768 MB cap | well under 1 GiB with a "medium" voice | 🟢 footprint / 🟡 cap | ONNX runtime + `en_US-lessac-medium`, downloaded on first boot |
| `cloudflared` (tunnel profile) | 128 MB cap | ~20–30 MiB idle | 🟢 | |
| `alloy` (logging profile) | 256 MB cap | — | 🟡 | Single-daemon log-tailing sidecar |
| `db` / `icecast` / `engine` / `api` / `admin_ui` | *(uncapped)* | modest (observed range: 10-130MB) | 🟡 | No limits configured; none has ever been the memory pressure point |

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

## 🍓 Raspberry Pi (gh-#213 spike — desk research, no Pi run yet)

Registry-manifest audits and published third-party benchmarks, collected 2026-07-29.
**No GenWave has ever run on a Pi** — everything below stays 🟡/🔴 until the hands-on runs land,
per this file's rules.

**Bottom line:** 🟡 a **Pi 5 8GB is a real target today for a piper-only station**
(`--pinned` images since v2.8.8 — no source build needed); the full Kokoro+LLM demo shape
fits on **no** Pi.

### 🚧 Image architecture — blockers resolved at v2.8.8

| Piece | arm64 today? | Confidence | Notes |
|---|:---:|:---:|---|
| Upstream bases (postgres, liquidsoap, dotnet, node, debian) | ✅ | 🟡 | Multi-arch manifests exist; we've never run one on ARM |
| ollama / cloudflared / alloy / dockerproxy / caddy | ✅ | 🟡 | Multi-arch manifests exist |
| GenWave GHCR images (api, admin_ui, engine, icecast, piper) | ✅ | 🟡 | **Multi-arch (amd64+arm64) since `home-v2.8.8`** — gh-#240 shipped native-ARM release builds and gh-#241 replaced the amd64-only artibex piper with the repo-owned `piper/` image (same wire shape). Both platforms verified on GHCR at the v2.8.8 cut; never yet *run* on ARM, hence 🟡 |
| `kokoro` (kokoro-fastapi-cpu) | ⚠️ | 🔴 | An arm64 manifest exists, but upstream #279 reports a warmup crash on Pi 4 aarch64 (open); zero Pi 5 data |

### 🔢 Compute (cited third-party numbers, not ours)

- 🟡 **Piper** — the proven ARM path: RTF **0.10–0.12** measured on RK3588 (same A76 cores as
  the Pi 5); Home Assistant's own "2 s of audio in 1 s" on a Pi 4 (RTF ~0.5).
- 🔴 **Kokoro** — Pi 4 measured RTF **3.19** (sherpa-onnx): no-go. Pi 5: **no published data**;
  the A72→A76 extrapolation lands ~RTF 1.0–1.5 — survivable inside a 120 s render budget but
  unproven, and shadowed by the warmup-crash report above.
- 🟡 **ollama `llama3.2:3b` on a Pi 5 8GB** — consensus **4–6 tok/s** across four independent
  benches → ~30–90 s per blurb, comparable to the demo box's fenced x86 core. Needs
  `Llm:TimeoutSeconds` 90–120, render budget 120, and an **active cooler** (thermal throttle
  halves tok/s within ~90 s without one). 🔴 Pi 4: the model won't even load in 4 GB.
- 🟡 **Enrichment burst** (first boot only): estimated **1–2 h per 1,000 tracks on a Pi 5**
  (4–8 h on a Pi 4) — set `Library:EnrichmentConcurrency: 2` and put the library on USB-SSD.

### 🧮 Memory budget

- 🟡 **Pi 5 8GB**: pick **at most one** of {kokoro, ollama}. Playout core + piper ≈ 1.2–1.8 GB
  (huge headroom); + kokoro fits; + ollama at the demo's 6 GB fence does NOT fit alongside
  kokoro — piper-only plus a ~4.5–5 GB fence is the untested middle (the live-observed OOM
  floor was 3 GB).
- 🟡 **Pi 4 4GB**: playout + piper only, no LLM.

### 🏆 Ranked topologies

| | Topology | Confidence | Shape |
|:---:|---|:---:|---|
| a | **Pi 5 8GB all-in-one "quiet DJ" — RECOMMENDED** | 🟡 | Playout + piper-only, no LLM (templated patter, by design). The shipped shape: `./launch.sh --pinned --piper-only` (gh-#242) on the v2.8.8+ multi-arch images. Every component is ARM-proven upstream; ours have never run on ARM |
| b | **Pi 5 playout + off-box brain — best sound** | 🟡 | Kokoro + ollama live on any x86 box; `Tts:Endpoint`, `Tts:Fallback:Endpoint`, and `Llm:Endpoint`/`Llm:Model` are all live-settable — verified pointable today |
| c | **Pi 5 all-in-one + LLM — experimental** | 🔴 | ollama fenced ~4.5–5 GB, piper-only, active cooler + 27 W PSU mandatory; the degradation ladder is the net. ⚠️ **Not expressible with the shipped flags as of gh-#310**: `--piper-only` now drops `ollama`/`ollama-init` along with kokoro (topology (a) was booting a resident model it could not hold). This shape needs its own overlay — an explicit opt-in, deliberately not a side effect of the low-memory one |
| d | **Pi 4 4GB headless minimal** | 🔴 | Works in principle; enrichment time is the pain |

### 🧪 Hands-on test plan (measurable outcomes)

The gh-#213 plan, condensed to what each step must produce before anything above turns 🟢:

1. **Arch pull check** — every `docker compose config --images` image pulls clean on arm64
   (v2.8.8+ pins; a failure here is a regression, file it).
2. **Pinned boot on Pi 5** — `./launch.sh --pinned --piper-only`; record pull + first-boot
   wall time. (Source-build timing is optional bonus data now, not the path.)
3. **Minimal playout boot** (the piper-only shape) — `/health` 200 and a gapless stream.
4. **Piper render timing** — RTF ~0.1–0.2 on Pi 5 (render seconds ÷ clip seconds).
5. **Enrichment burst on a real library** — tracks/hour, `vcgencmd measure_temp`, zero
   safe-branch engagements.
6. **The open Kokoro question** — does the arm64 image survive warmup on a Pi 5? If yes:
   RTF + RSS; if no: report upstream #279.
7. **ollama tok/s** — prompt/eval rates on a real DJ prompt; abort below 3 tok/s.
8. **24 h piper-only soak** — 0 safe-library engagements, 0 restarts, flat memory, temps,
   render-seconds vs budget.
9. **Pi 4 pass** — steps 1–5 only.

Results land as 🟢 rows in **Known deployments** above — problems are as valuable as successes.

## ✅ Software requirements

| Requirement | Value | Confidence |
|---|---|:---:|
| OS | Linux with Docker Engine (the only deployment shape ever run) | 🟢 |
| Docker Compose | **v2.24+** (the demo overlay uses the `!override` merge tag) | 🟢 |
| GPU | none — not used anywhere | 🟢 |

## 🤝 Contributing an entry

1. Run the stack (`./launch.sh`, or the `--pinned` appliance flow — see [DEPLOYMENT.md](DEPLOYMENT.md)).
2. Note CPU model, core count, RAM, storage, and which profiles you ran (`--pinned`, `admin`, `logging`,
   `tunnel`, the demo LLM overlay).
3. PR a row into **Known deployments** with 🟢 for what you verified and a note for anything that
   needed tuning (e.g. a different ollama fence). Problems are as valuable as successes — file
   them as issues and reference them from a 🔴 row.
