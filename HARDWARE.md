# 🖥️ Hardware Compatibility

What GenWave actually runs on, what each service needs, and the confidence level for every claim.
This file is the source of truth for hardware guidance (gh-#20) — **contribute your own box via
PR**: add a row to the deployments table with your specs and what worked (or didn't).

**Looking for a quick answer?** Everything scannable is up top. The narrative — Pi setup, compute
notes, test status — is below it.

| 📊 The data | 📖 The detail |
|---|---|
| [Confidence legend](#-confidence-legend) | [Raspberry Pi setup](#-raspberry-pi-setup) |
| [Known deployments](#-known-deployments) | [Compute notes](#-compute-notes-raspberry-pi) |
| [Sizing at a glance](#-sizing-at-a-glance) | [Design target](#-design-target) |
| [What each service needs](#-what-each-service-needs) | [Hands-on test plan](#-hands-on-test-plan) |
| [Software requirements](#-software-requirements) | [Contributing an entry](#-contributing-an-entry) |
| [Raspberry Pi topologies](#-raspberry-pi-topologies) | |
| [Raspberry Pi arm64 image support](#-raspberry-pi-arm64-image-support) | |

## 🎨 Confidence legend

| Mark | Meaning |
|:---:|---|
| 🟢 | **Verified** — GenWave has demonstrably run here, or the number was measured/observed live |
| 🟡 | **Expected** — derived from configured limits or design targets; not independently measured |
| 🔴 | **Unverified / known-problematic** — no test has been run, or a problem was observed |

## 📦 Known deployments

### Computer Systems
| Machine | Arch | Core/vCPU Count | RAM | Storage | OS | GenWave | Role | Status | Notes | Verifier |
|---|---|:---:|:---:|:---:|---|:---:|---|:---:|---|---|
| `demo.genwaveradio.com` appliance (CCX23) | x86-64 | 4 | 16GB | 80GB | Ubuntu 26.04 LTS | `home-v5.2.1` (tracks each release) | Public demo station, full stack + admin + LLM + tunnel + logging | 🟢 | Runs the pinned release 24/7 (health-probed by CI). Source of the one live-observed sizing fact: ollama at a 3 GB fence OOM-killed constantly; stable at **1 CPU / 6GB** (observed 2026-07-21, v2.2.0 rollout) | GenWave |
| Development machine | x86-64 | 112 | 512GB | 4TB | Ubuntu 25.04 | source (`main`) | `./launch.sh` dev flow, full stack from source | 🟢 | Docker on Dell Precision 7920 Tower | GenWave |
| Raspberry Pi 5 Model B Rev 1.0 | **arm64** | 4 | **4GB** | 256GB NVMe via M.2 HAT+, no SD card | Debian 13 trixie | `home-v2.9.1` (last verified) | Piper-only playout appliance, `./launch.sh --pinned --piper-only` | 🟢 | **First ARM deployment** (2026-08-02, `home-v2.9.0`). Gapless stream with no stutters; 66–69 °C flat under a full enrichment burst; `vcgencmd get_throttled` = `0x0` at 2.4 GHz uncapped. **Measured under 4-core enrichment load**: piper **RTF 0.252**, enrichment **~800 tracks/h**, and **zero mid-broadcast safe-branch engagements** across 1 h 40 m. 9,094-track library over NFS. Kernel 6.12 (16k pages). Three hard prerequisites — see [Raspberry Pi setup](#-raspberry-pi-setup): stock CPU clock, `cgroup_enable=memory`, official 27 W PSU | GenWave |
| Raspberry Pi 4 Model B Rev 1.1 | **arm64** | 4 (A72, stock 1.5 GHz) | **4GB** | **16GB SD card** (ext4) | Debian 12 bookworm | `home-v5.2.1` | Piper-only playout appliance, `./launch.sh --pinned --piper-only` | 🟢 | **7-day soak PASSED** (2026-08-09 → 08-17, `home-v3.3.2`, closed at **7 d 10 h 51 m**): `get_throttled=0x0` the entire boot, **0 restarts / 0 OOM kills** on all 8 containers, **api 0 warn/error lines per 24 h at every checkpoint**, swap untouched, load ~0.7 idle / ~1.6 mid-decode. Memory plateaued (engine ~270 MiB, api ~255 MiB); piper parked at ~405–411 MiB for six days then **stepped to ~498 MiB on the final day** — real heap, flat on re-sample, no OOM; watch item, not a fail (768 MiB cap holds). Same 9,094-track library **over NFS** — the media transport is load-bearing for the boundary-slip baseline, see [Compute notes](#-compute-notes-raspberry-pi). Upgraded to `home-v5.2.1` post-soak, airing verified same day | GenWave |

### Internet Radios
| Make | Model | Status | Notes | Verifier |
|---|---|:---:|---|---|
| Grace Digital | Mondo Elite Classic | 🟢 | Works great! | GenWave |

*Have GenWave running somewhere else — a NUC, an old laptop, a VPS? Add it here!*

## 📐 Sizing at a glance

| Shape | RAM | Status | Notes |
|---|:---:|:---:|---|
| **Playout + piper TTS** — no kokoro, no LLM | **4 GB** | 🟢 | Measured, not derived: a 4 GB Pi 5 runs this with the whole stack resident and room to spare. Enrichment is the peak and stays under ~500 MiB |
| **+ kokoro**, no LLM | 8 GB | 🟡 | kokoro's ~1.2 GiB baseline (leaking toward a 4 GB cap) is the biggest single resident. 4 GB is too tight |
| **+ LLM resident** — the demo shape | 8 GB min, 16 GB comfortable | 🟡 | The 6 GB ollama fence plus kokoro's baseline already crowds an 8 GB box. Which model: any in the [LLM model floor](#-llm-model-floor) table is truthful — size it to the fence |

- 🟢 **CPU** — four cores is enough for playout plus piper TTS. **Enrichment is the constraint, not
  playout**: the ffmpeg analyzers use every core you give them (`Library:EnrichmentConcurrency`,
  default 4, and 2 under the piper-only overlay). More cores mainly make the first-boot catalog
  build finish sooner.
- 🟢 **Disk** — your music library (bind-mounted **read-only**) plus modest named volumes
  (Postgres data, rendered TTS segments, Piper models). Size to the library.
- 🟢 **First-boot catalog build** — budget for it. Enrichment analyses every track once. Measured
  on a 4 GB Pi 5: **~800 tracks/hour at concurrency 4** and **~700 at 2** — roughly **11 h** and
  **13 h** respectively for a 9,000-track library. ⚠️ Pi boxes run `--piper-only`, which
  **defaults to 2**, so 13 h is the number most Pi operators should plan against. It runs in the
  background and the station broadcasts throughout — a planning number, not a blocker. Faster
  hardware, or a higher `LIBRARY_ENRICHMENT_CONCURRENCY`, shortens it.

## 🧩 What each service needs

Configured limits come from `compose.yaml` / `compose.demo.yaml`; "real footprint" values are the
notes recorded alongside them.

> ⚠️ **On Raspberry Pi kernels these limits do nothing until you enable the memory cgroup**
> (gh-#307). Pi kernels ship with it off, and Docker discards every `mem_limit` **silently** — no
> warning, no error, the fence simply is not there. See [Raspberry Pi setup](#-raspberry-pi-setup)
> step 2.

| Service | Configured limit | Real footprint | Confidence | Notes |
|---|:---:|---|:---:|---|
| `kokoro` (TTS) | 4 GB cap | ~1.2 GiB fresh baseline; leaks toward the cap under render load (upstream `#262`), long renders spike ~+0.5 GiB | 🟢 | Cap is a fail-closed backstop. Live-observed: 3 GB cap = OOM-bounce every ~24–30 h on the demo box, down to ~90 min in heavy render windows (gh-#276); 4 GB buys spike headroom over the leaked baseline |
| `ollama` (DJ brain, demo profile) | **1 CPU / 6GB fence** | needs > 3 GB with `llama3.2:3b` resident (`KEEP_ALIVE=-1`) | 🟢 | Live-observed: 3 GB fence = constant OOM kills. Cold model load ~25 s+; a full persona prompt on one fenced core takes ~25–30 s even warm — set `Llm:TimeoutSeconds: 60`. Size the model to the fence — truthfulness is not the constraint, see [LLM model floor](#-llm-model-floor) |
| `piper` (fallback TTS) | 768 MB cap | ~165–220 MiB with a "medium" voice | 🟢 | ONNX runtime + `en_US-lessac-medium`, downloaded on first boot. Footprint measured on both x86-64 and arm64 (Pi 5, 2026-08-02) |
| `api` (incl. enrichment) | *(uncapped)* | ~60 MiB idle; **400–500 MiB during an enrichment burst**, sawtoothing under GC | 🟢 | Measured on the Pi 5 run. RSS stays flat while analyzed bytes climb — transient peaks near 1 GiB are pre-collection, not a floor. Enrichment is by far the heaviest sustained load GenWave produces |
| `cloudflared` (tunnel profile) | 128 MB cap | ~20–30 MiB idle | 🟢 | |
| `alloy` (logging profile) | 256 MB cap | — | 🟡 | Single-daemon log-tailing sidecar |
| `db` / `icecast` / `engine` / `admin_ui` | *(uncapped)* | modest (observed range: 5–130 MB) | 🟡 | No limits configured; none has ever been the memory pressure point |

## 🧠 LLM model floor

**The truth bench — T336, 2026-08-21 → 08-28.** The question (gh-#438): when the DJ named the
wrong weekday against an explicit clock line, was that model *size*, model *family*, or *prompt
shape* — and how small a model is safe to run? The method (SPEC F138.7): every gated LLM call
carries a typed outcome counted per model (F139), so a week of ordinary playout with the model
rotated on the Settings page *is* the bench — no hand-scored battery. The floor signal is the
**truth-gate reject share of copy calls**: the gate caught an invented fact, a wrong day, or a
fabricated verifiable, re-asked once, and counted the miss.

| | |
|---|---|
| Build | `main` @ `257c801` (PR #609 merge) — api image built 2026-08-21 12:25Z, up 13:23Z, no restart for the week |
| Inference | Ollama 0.32.5 on an **RTX 4090** (external `Llm:Endpoint`) — a GPU, so this measures **truthfulness, not capacity** |
| Settings | `Llm:MaxCopyChars` 750 · `Llm:TimeoutSeconds` 30 · crosstalk target 50 s |
| Workload | The dev station's normal playout, ~1.1k copy calls/day (replan-inflated by gh-#610's doubled library at the time — constant across the week, so a fair comparison) |
| Capture | `genwave-T336.sh snapshot` on cron ×2/day → `snapshots.jsonl` (the counters roll on a 24 h window and never persist — F139.3) |

| Model | Bench days | Copy calls | Gate rejects | Share | |
|---|---|---:|---:|---:|:---:|
| `llama3.1:8b` | 08-21 → 22 | ~1,137 | 0 | 0.0% | 🟢 |
| `llama3.2:3b` (the demo default) | 08-22 → 24 | ~2,365 | 7 | 0.30% | 🟢 |
| `gemma3:12b` | 08-24 → 26 | ~2,152 | 0 | 0.0% | 🟢 |
| `mistral-nemo:12b` | 08-26 → 27 | ~1,103 | 3 | 0.27% | 🟢 |
| `mistral:7b` | 08-27 → 28 | ~1,001 | 3 | 0.30% | 🟢 |
| `gemma4:12b` | 08-24, 31 min | 15 | 15 empty | — | excluded: gh-#620, a client bug (fixed in v5.4.0), not a verdict |

Counts are de-overlapped 24 h windows. The script's `report` sums daily maxima of *trailing*
windows and over-counts totals by ~1.3–1.5×; the shares are unaffected.

- 🟢 **Every model benched is above the floor — the floor sits below the smallest model tested.**
  The worst share, 0.3%, is ~3 gate re-asks a day on ~1,100 calls; the gate's single re-ask
  absorbs those, so copy that actually lands on a template is rarer still.
- 🟢 **The answer to #438 is prompt shape — not size, not family.** 3B ≈ 7B ≈ 12B at ~0.3%;
  `llama3.1:8b` and `gemma3:12b` at zero is a real difference statistically and an inaudible one
  on air. The lever was the F138 truth gate plus the F141 honest clock line, shipped to every
  model equally.
- 🟢 **Pick the DJ model by what your ollama fence can hold and answer inside
  `Llm:TimeoutSeconds` — not by truthfulness.** `llama3.2:3b`, the demo default, is not a
  compromise on truth.
- 🟡 **Capacity floor: still the one CPU fact in the table above** (`llama3.2:3b`, ~25–30 s warm
  on 1 CPU / 6 GB). Zero timeouts and zero over-length rejects all week is a *GPU* result. The
  homelab bench (gh-#626) runs this same script on a CCX23-class box (4 vCPU / 16 GB / no GPU)
  under the shipped demo profile and reads both floors from the same counters.
- 🟡 **Crosstalk lane unmeasured**: 4 calls all week (`llama3.1:8b`, 1 malformed). The
  malformed-shape floor is an open question.
- 🔴 **Thinking models** (`gemma4`, `qwen3`, `deepseek-r1`, `magistral`) returned empty
  completions under this build (gh-#620) — a client limitation, fixed in v5.4.0. `gemma4:12b`'s
  own verdict — the gh-#365 flap history — waits for a GPU-owner spot-check after that ships.

## ✅ Software requirements

| Requirement | Value | Confidence |
|---|---|:---:|
| OS | Linux with Docker Engine (the only deployment shape ever run) | 🟢 |
| Docker Compose | **v2.24+** (the demo overlay uses the `!override` merge tag) | 🟢 |
| Architecture | x86-64 or arm64 (multi-arch images since `home-v2.8.8`) | 🟢 |
| Raspberry Pi kernel | `cgroup_enable=memory cgroup_memory=1` in `cmdline.txt`, or every `mem_limit` is silently discarded (gh-#307) | 🟢 |
| GPU | none — not used anywhere | 🟢 |

## 🏆 Raspberry Pi topologies

**Bottom line:** 🟢 a **Pi 5 with 4 GB runs a piper-only station** — proven, not projected:
`./launch.sh --pinned --piper-only` on the `home-v2.8.8+` multi-arch images. Measured 2026-08-02
over 1 h 40 m: gapless broadcast at 66–69 °C with clean power, piper rendering **4× faster than
real time** and enrichment chewing through **~800 tracks/hour** — *simultaneously*, on four cores.
The safe branch never engaged after boot. **A Pi 4 4GB runs the same topology** — proven by a
7-day soak (2026-08, `home-v3.3.2`, zero restarts, zero throttling), on a 16GB SD card. The full
Kokoro+LLM demo shape fits on **no** Pi.

| | Topology | Confidence | Shape |
|:---:|---|:---:|---|
| a | **Pi 5 all-in-one "quiet DJ" — RECOMMENDED** | 🟢 | Playout + piper-only, no LLM. **Verified on 4 GB**, 2026-08-02. The shipped shape: `./launch.sh --pinned --piper-only` (gh-#242) on the v2.8.8+ multi-arch images. ⚠️ "No LLM" costs more than patter — see below |
| b | **Pi 5 playout + off-box brain — best sound** | 🟡 | Kokoro + ollama live on any x86 box; `Tts:Endpoint`, `Tts:Fallback:Endpoint`, and `Llm:Endpoint`/`Llm:Model` are all live-settable — verified pointable, but never run in this split |
| c | **Pi 5 all-in-one + LLM — experimental** | 🔴 | ollama fenced ~4.5–5 GB, piper-only, active cooler + 27 W PSU mandatory; the degradation ladder is the net. ⚠️ **Not expressible with the shipped flags as of gh-#310**: `--piper-only` now drops `ollama`/`ollama-init` along with kokoro (topology (a) was booting a resident model it could not hold). This shape needs its own overlay — an explicit opt-in, deliberately not a side effect of the low-memory one. Restores mood tagging and explicit classification |
| d | **Pi 4 4GB headless minimal** | 🟢 | Same shape as (a) on slower silicon — **proven to stay working: 7-day soak PASSED on `home-v3.3.2`** (2026-08, see Known deployments), and on a 16GB SD card at that. Enrichment throughput on the A72 was never separately measured (the soak box's catalog was already built); plan for slower-than-Pi-5, not for broken |

### ⚠️ What "no LLM" actually costs (gh-#336)

Topologies (a), (b) and (d) run with `Llm:Endpoint` empty — the documented disabled state
(F34.2). That is by design and the station broadcasts perfectly well, but it disables **three**
things, not one:

| | Consequence |
|---|---|
| **Patter** | Falls back to the template writer. A quality tradeoff, chosen deliberately |
| **Mood tagging** | Never populated — anything selecting on mood has less to work with |
| **Explicit classification** | Never populated. ⚠️ Unclassified tracks currently **stay in rotation** (`coalesce(m.explicit, false)`) — tracked as gh-#337 |

If your library depends on automatic explicit classification, topology (c) or an off-box LLM
(topology b) is the shape you want.

## 🚧 Raspberry Pi arm64 image support

| Piece | arm64 today? | Confidence | Notes |
|---|:---:|:---:|---|
| Upstream bases (postgres, liquidsoap, dotnet, node, debian) | ✅ | 🟢 | All five ran on the Pi 5, 2026-08-02 |
| GenWave GHCR images (api, admin_ui, engine, icecast, piper) | ✅ | 🟢 | **Multi-arch (amd64+arm64) since `home-v2.8.8`** — gh-#240 shipped native-ARM release builds, gh-#241 replaced the amd64-only artibex piper with the repo-owned `piper/` image (same wire shape). All five pulled and ran on arm64 |
| caddy / dockerproxy | ✅ | 🟢 | Both ran on the Pi 5 |
| ollama / cloudflared / alloy | ✅ | 🟡 | Multi-arch manifests exist; not exercised on ARM (piper-only topology, no tunnel or logging profile) |
| `kokoro` (kokoro-fastapi-cpu) | ⚠️ | 🔴 | An arm64 manifest exists, but upstream #279 reports a warmup crash on Pi 4 aarch64 (open); still zero Pi 5 data — the piper-only topology never starts it |

---

## 🍓 Raspberry Pi setup

Field-verified 2026-08-02 on a Pi 5 4GB (gh-#213 spike → gh-#307/#308 hardening). The 4 GB result
matters: the entry price for topology (a) is lower than this file previously claimed (it said 8 GB).

### 🔑 SSH access — key-only, in the right order

A playout appliance is a headless box you'll be reaching for at odd hours; set key auth up first
and turn password auth off. The **order matters exactly once**: verify the key works *before*
disabling passwords, or you've locked yourself out of a machine with no screen.

```bash
# From your workstation — copies your public key (add -i ~/.ssh/id_ed25519.pub to pick one):
ssh-copy-id <user>@<pi-ip>

# Verify key login works BEFORE the next step:
ssh <user>@<pi-ip>

# On the Pi — key login confirmed? Now close password auth:
sudo sed -i 's/^#\?PasswordAuthentication.*/PasswordAuthentication no/' /etc/ssh/sshd_config
sudo systemctl restart ssh

# Optional, workstation: an alias so it's just `ssh <name>` forever
cat >> ~/.ssh/config <<'EOF'
Host <name>
    HostName <pi-ip>
    User <user>
EOF
```

`tools/soak-check.sh` assumes exactly this shape — `ssh <box> 'bash -s' < tools/soak-check.sh`
is the whole remote checkpoint.

### 🔧 Prep — do these before anything else

Every one of these was learned the hard way. Steps 1–3 each caused a failure that looked like a
GenWave bug and was not.

**1. 🚨 Stock CPU clock — check this before trusting any measurement (gh-#308).**

```bash
grep -E "arm_freq|over_voltage|force_turbo" /boot/firmware/config.txt   # want NO output
```

An overclock is the single most destructive thing you can do to a Pi running this stack. A test
box at `arm_freq=2900` (stock is 2400) undervolted roughly once every two minutes under
enrichment, ran at 83.4 °C, and had already been clock-capped back to 2.70 GHz by the 80 °C soft
limit — paying full overclock current and heat **for clocks it never got**. Removing it took the
same box to 69.2 °C and zero undervoltage events under identical load.

A brownout reset mid-enrichment is how this ends, and it corrupts whatever was mid-write.

**2. 🧠 Enable the memory cgroup (gh-#307).** Pi kernels ship with it disabled, and Docker
discards every `mem_limit` **silently** — the piper 768 MB fence and the kokoro 4 GB backstop
simply do not exist until you fix this. Append to `/boot/firmware/cmdline.txt` (one line, no
newlines) and reboot:

```
cgroup_enable=memory cgroup_memory=1
```

Verify — the limit column must show the fence, not total RAM:

```bash
docker stats --no-stream    # piper should read e.g. 220MiB / 768MiB, not / 3.953GiB
```

⚠️ **If the stack ever ran before this fix, a reboot is not enough** (learned live on the second
Pi, 2026-08-09): the containers were *created* with the discarded (zero) limit baked into their
config, Docker's restart policy resurrects them unfenced after the reboot, and a plain `up -d`
sees no compose diff so it recreates nothing. Force the recreate:

```bash
docker compose down          # COMPOSE_FILE in .env carries your overlays (gh-#309)
./launch.sh --pinned --piper-only
docker inspect <project>-piper-1 --format '{{.HostConfig.Memory}}'   # want 805306368
```

**3½. 📻 LAN-only listening — scheme-prefix `PUBLIC_HOST`.** The Caddyfile's site address is the
raw `{$PUBLIC_HOST}`, and a bare hostname (or `localhost`) gets Caddy's full auto-HTTPS
treatment: HTTP 308-redirects to HTTPS with a self-signed cert nothing else trusts — from
another machine the stream is simply unreachable. For a LAN test box, prefix the scheme to turn
all of that off:

```bash
PUBLIC_HOST=http://<lan-name>     # in .env — plain HTTP on :80, no redirect, no cert
```

Stream at `http://<lan-name>/stream`, spectator page at `http://<lan-name>/` — hardware radios
tune it directly. Public boxes keep the bare domain form; TLS issuance is the whole point there.

**3. 🔌 Official Raspberry Pi 27 W (5 V/5 A) PSU.** Not "a 27 W supply" — the official unit.
A branded third-party kit PSU on the test box negotiated 5 A correctly and *still* sagged under
load transients, logging 12+ undervoltage events in the first hour and eventually hard
power-cutting the box mid-enrichment. **With an NVMe HAT this is a hard requirement, not a
recommendation** — the drive adds 2–4 W steady with higher write spikes, and enrichment stacks a
multi-core CPU burst against a Postgres WAL write burst on that same drive.

```bash
od -An -tu4 --endian=big /proc/device-tree/chosen/power/max_current   # 5000 = 5V/5A negotiated
```

⚠️ That file is **big-endian**. `hexdump -e '1/4 "%d\n"'` and a bare `od -tu4` read native and
silently return `-2012020736` instead of `5000`. The `--endian=big` is load-bearing.

**4. ❄️ Active cooler.** Not optional. The Pi 5 soft-limits at 80 °C and hard-limits at 85 °C,
and enrichment will hold every core busy for the length of the first catalog build. A correctly
clocked box with an active cooler plateaus around 69 °C under that load.

**5. 💾 Storage.** NVMe via the M.2 HAT+ is the tested path (also verified: media over NFS). An
SD card works but makes the power-cut corruption story much worse — ext4 journal plus Postgres
WAL on NVMe recover far more reliably. That said, the risk is power-cut corruption, not
steady-state wear: the Pi 4 soak box ran **7 days on a 16 GB SD card** without incident.

### ✅ Proving the box before you trust any numbers

Run a real load burst (start the stack and let enrichment run), then:

```bash
vcgencmd get_throttled     # want 0x0 — this is the gate
vcgencmd measure_temp      # want well under 80'C
vcgencmd measure_clock arm # want ~2.4GHz, i.e. not being capped
dmesg | grep -c Undervoltage
```

`get_throttled` = `0x0` **after a load burst** is the criterion — not the absence of dmesg lines
at idle. The sticky bits reset at boot, so the reading is only meaningful against a known boot
and a known load.

⚠️ The kernel string is `Undervoltage detected!`. Grepping for `Undervolted` returns `0` on a box
that is actively undervolting — and `0` is exactly the result you are hoping for, so the typo
reads as a pass.

Decoding `get_throttled`:

| bit | meaning | | bit | meaning |
|:---:|---|---|:---:|---|
| 0 | under-voltage **now** | | 16 | under-voltage **has occurred** |
| 1 | Arm freq capped **now** | | 17 | freq capping **has occurred** |
| 2 | throttled **now** | | 18 | throttling **has occurred** |
| 3 | soft temp limit **now** | | 19 | soft temp limit **has occurred** |

### 🌐 Reaching the stream on a LAN (no public hostname)

The pinned/demo topology is fail-closed by design: icecast has no host port at all
(`ports: !override []`), the api is loopback-only, and Caddy answers **only** for `${PUBLIC_HOST}`.
A test box left on the `.env.example` placeholder is unreachable from every other machine on the
network.

```bash
# .env on the Pi — the http:// prefix makes Caddy serve plain HTTP on :80 with no ACME attempt
PUBLIC_HOST=http://192.168.2.127
```

Then `http://<pi-ip>/stream` (Caddy → icecast), `http://<pi-ip>/` (spectator surface),
`http://<pi-ip>/health`. 🟢 Verified 2026-08-02 with a LAN VLC client; Caddy's own log confirms
the no-ACME path (`server is listening only on the HTTP port, so no automatic HTTPS will be
applied`). This is also the *right* way to run the gapless check — real listeners go through
Caddy, so the test should too.

Caddy answers only the literal `PUBLIC_HOST` value: connect by that exact IP, or comma-separate
multiple hosts in the variable.

## 🔢 Compute notes (Raspberry Pi)

- 🟢 **Piper — measured RTF 0.252 on a Pi 5** (2026-08-02, `en_US-lessac-medium`, six renders of
  4-second DJ lines, spread 0.246–0.265). **That number is under load**: taken with enrichment
  saturating all four cores (api at 381%, load average 8.8). It is above the 0.10–0.12 that
  third-party RK3588 benches predicted — those were unloaded boxes, and this is the realistic
  first-boot condition. At RTF 0.25 piper still renders **4× faster than real time**, which is why
  the safe branch never engaged mid-broadcast. The uncontended figure was not measured and would
  be better.
- 🔴 **Kokoro** — Pi 4 measured RTF **3.19** (sherpa-onnx): no-go. Pi 5: **no published data**;
  the A72→A76 extrapolation lands ~RTF 1.0–1.5 — survivable inside a 120 s render budget but
  unproven, and shadowed by the warmup-crash report above.
- 🟡 **ollama `llama3.2:3b` on a Pi 5 8GB** — consensus **4–6 tok/s** across four independent
  benches → ~30–90 s per blurb, comparable to the demo box's fenced x86 core. Needs
  `Llm:TimeoutSeconds` 90–120, render budget 120, and an **active cooler** (thermal throttle
  halves tok/s within ~90 s without one). 🔴 Pi 4: the model won't even load in 4 GB.
- 🟢 **Enrichment — measured ~800 tracks/hour on a Pi 5** at `EnrichmentConcurrency: 4`
  (2026-08-02): 802 tracks/h across a 158-minute burst, 773 tracks/h over the last hour of it,
  3–20 tracks per minute. **This validates the desk estimate** — 1–2 h per 1,000 tracks predicted
  500–1,000/h, and the measurement lands squarely inside it.
- 🟢 **Enrichment saturates every core it is given.** The api held 310–381% of 400% for the whole
  burst. The **piper-only overlay defaults this to 2** (gh-#334), buying two free cores and lower
  thermals — set `LIBRARY_ENRICHMENT_CONCURRENCY` to override on either stack.
- 🟢 **Engine memory: plateau, not leak — SETTLED** (replacing the 2026-08-03 "unresolved" note,
  as promised). The multi-day extension answered it: engine climbed 169 → 289 MiB then **fell back
  and held** (265 MiB at the ~70 h mark of the Pi 5 run; **269.6 MiB at day 7** of the 2026-08
  Pi 4 soak). The climb is warm-up, not a slope — no `mem_limit` needed. Piper is the one to
  keep an eye on instead: see the soak-runbook reference numbers below.
- 🟡 **NFS media is load-bearing for the boundary-slip baseline.** Both Pi test boxes mount music
  over NFS — **local-storage behavior is unmeasured**. The engine's boundary-slip log family (an
  internal catch-up; zero audible effect in a week of ear checks) runs at an intrinsic **~18–33/h
  on the Pi 4's A72** and correlates with FLAC-decode-over-NFS: it fell 36–40/h → ~18/h once
  enrichment catch-up finished, and tracks decode load. Treat the rate as a per-box baseline to
  compare against across checkpoints — a drift (the Pi 4 crept 20 → 33/h over its 7-day soak) is
  a note; a spike is a question; neither has ever been a dropout.
- 🟢 **Concurrency 2 costs far less than half — measured ~700 tracks/hour** on the same 4 GB Pi 5
  (2026-08-03): 6,303 tracks over a 9-hour sustained run, flat at 654–766/h per hour with no gaps,
  api at ~234% of 400%. So **9,000 tracks is ~13 h at 2, not the ~22 h a linear model predicts**
  — halving the workers does not halve throughput, because the analyzers are not purely CPU-bound
  (NFS media reads, catalog writes). ⚠️ This corrects an earlier "expect proportionally slower at
  2" note here and a matching ~22 h estimate in DEPLOYMENT.md; `tools/check-doc-drift.sh` cannot
  catch this class, since measured figures have no compose file to diff against.
- 🟢 **The safe branch never engaged mid-broadcast.** Across 1 h 40 m the engine's complete switch
  history is five lines, all inside the first **nine seconds** of boot: `mksafe → safe_blank`
  (silence while the feeder warms), one `safe_lib` prefetch resolution, ~3 s on the safe branch,
  then `switch.1 → metadata_deduplicate` and the main queue holds forever. That cold-start ladder
  is the designed never-silent behaviour, not a failure. `safe_lib` resolved exactly one track,
  ever.

## 🎯 Design target

GenWave's stated hardware goal:

> **A modest CPU-only box runs the whole station; features that require a GPU don't ship in year one (hopefully never).**

- 🟢 **CPU-only by design** — TTS uses the CPU builds (`kokoro-fastapi-cpu`, Piper ONNX); the demo
  LLM (`llama3.2:3b` via ollama) runs CPU inference on one fenced core. No GPU is used anywhere.
- 🟢 **x86-64 and ARM64 both run** — the GHCR images are multi-arch since `home-v2.8.8`
  (gh-#240 native-ARM release builds, gh-#241 the repo-owned piper image), and ARM64 was
  field-verified on a Raspberry Pi 5 on 2026-08-02.

## 🧪 Hands-on test plan

The gh-#213 plan and where it stands after the 2026-08-02 field run:

| | Step | Status |
|:---:|---|:---:|
| 1 | **Arch pull check** — every `docker compose config --images` image pulls clean on arm64 | 🟢 all five GHCR images + upstreams pulled clean |
| 2 | **Pinned boot on Pi 5** — `./launch.sh --pinned --piper-only` | 🟢 booted; pull + first-boot wall time not recorded |
| 3 | **Minimal playout boot** — `/health` 200 and a gapless stream | 🟢 **1 h 40 m+ continuous**, no stutters or gaps, throughout an enrichment burst |
| 4 | **Piper render timing** — RTF ~0.1–0.2 on Pi 5 (render seconds ÷ clip seconds) | 🟢 **RTF 0.252** — measured under 4-core enrichment load, so above the projected band; still 4× faster than real time. Uncontended not measured |
| 5 | **Enrichment burst on a real library** — tracks/hour, `vcgencmd measure_temp`, zero safe-branch engagements | 🟢 **~800 tracks/h** at concurrency 4; 66–69 °C flat; **zero mid-broadcast safe-branch engagements** over 1 h 40 m |
| 6 | **The open Kokoro question** — does the arm64 image survive warmup on a Pi 5? | 🔴 not attempted |
| 7 | **ollama tok/s** — prompt/eval rates on a real DJ prompt; abort below 3 tok/s | 🔴 not attempted |
| 8 | **24 h piper-only soak** — 0 safe-library engagements, 0 restarts, flat memory, temps, render-seconds vs budget | 🟢 **PASSED — 25 h 36 m**, 2026-08-03T00:04Z → 2026-08-04T01:40Z on `home-v2.9.1`. **0 restarts** on all 8 containers; **0 mid-broadcast safe-branch engagements** — the engine's entire switch history is still the 5 boot-ladder lines from 00:04; `vcgencmd get_throttled` = `0x0` at **51.0 °C**; enrichment 9,094/9,094 intact. **5 real error lines across the final 16 h** (4× cosmetic `mjpeg: error decoding EXIF data` from malformed album art, 1× icecast metadata `ECONNRESET`). ⚠️ Memory is not uniformly flat — see [Compute notes](#-compute-notes-raspberry-pi) |
| 9 | **Pi 4 pass** — steps 1–5 only | 🟢 steps 1–3 via the 2026-08 **7-day soak** (`home-v3.3.2` and later `home-v5.2.1` pulled clean on arm64; pinned boot; week-long broadcast, ear-checked). Steps 4–5 (piper RTF, enrichment tracks/h) were **not** re-measured on the A72 — those numbers remain Pi 5 figures |

Steps 6 and 7 remain; both matter only for topology (c). Step 8's bar has since been cleared
twice over — the 25 h Pi 5 soak below, then the Pi 4's 7-day run (see Known deployments).

Results land as 🟢 rows in **Known deployments** above — problems are as valuable as successes.

## 🔁 Soak runbook — the identical test on any box

The step-8 soak (and its multi-day extension) as a repeatable procedure, so a Pi 4 — or any
future appliance — runs exactly what the Pi 5 ran. One script owns the checkpoint:
**`tools/soak-check.sh`** (read-only; every criterion below is a ✅/❌ line in its output).

**Before starting** 🔍
- Pi only: confirm `/boot/firmware/config.txt` carries **no `arm_freq`/`over_voltage` lines** —
  the 2026-08-02 "undervoltage" hunt was an overclock all along; no measurement is trustworthy
  over one. (The script checks this too, but check *before* burning a week.)
- Free disk sanity: old release images accumulate ~1.5 GB per release and nothing prunes them
  (gh-#441 — 46 GB of dead tags killed a demo-box deploy). On SD-card boxes run
  `docker system df` first.

**Start** 🚀 — `./launch.sh --pinned --piper-only` (topology (a)). Note the UTC boot time; every
checkpoint compares against it.

**Checkpoints** 📋 — run at ~9 h, ~24 h, and daily thereafter to 7 days:

```bash
./tools/soak-check.sh                       # on the box
ssh <box> 'bash -s' < tools/soak-check.sh   # or from a workstation
```

**Pass criteria** (what the script enforces — same bar step 8 used):
- 0 container restarts, 0 OOM kills, everything `running`
- `get_throttled=0x0` (Pi), swap untouched, root disk < 80 %
- `/health` 200; stream truth green (see gotchas)
- **0 mid-broadcast safe-branch engagements** — switch history stays the ~5 boot-ladder lines;
  the script counts only switches *away* from the main queue after T0+180 s
- 0 render-budget drops; feeder refill holds well under `Tts:RenderBudgetSeconds`
- Memory **plateau, not slope**: record the `docker stats` snapshot each checkpoint and compare
  — the Compute-notes table above holds the Pi 5 reference numbers (engine plateaus high-200s
  to ~300 MiB; piper crept 289 → 408 → 421 MiB over two weeks, decelerating, cap 768 MiB).
  Pi 4 7-day reference (2026-08, `home-v3.3.2`): piper parked ~405–411 MiB for six days then
  **stepped +87 MiB to ~498 on day 7** — real heap (`RssAnon`, not page cache), flat on a 20 s
  re-sample, no OOM. **A step reads as a watch item, not a fail**: re-check next day to
  discriminate step-plateau from resumed growth. A fresh `home-v5.2.1` piper starts ~262 MiB

**Gotchas that have burned us** ⚠️ (all encoded in the script, listed so nobody "fixes" them):
- Icecast's public `status-json.xsl` shows **no mounts by design** (F67 hardening). "No source
  connected" there is NOT an outage. Stream truth = `admin/metadata` lines in icecast's own log
  + an ESTABLISHED socket on :8000 via `/proc/net/tcp` (`:8000` is deliberately not
  host-published — never probe `localhost:8000` from the host).
- Engine/liquidsoap log lines carry container-local time (UTC/BST skew) — compare timestamps
  only via `docker logs -t` daemon-side UTC.
- `docker exec` runs **inside the target's cgroup**: near a `mem_limit` it can evict page cache
  and move the number being measured (observed live on alloy, 2026-08-09). Tiny execs only.
- Known-benign error lines: `mjpeg` EXIF decode on bad album art; `wav` max-data-size on TTS
  clips; a rare one-off icecast metadata `ECONNRESET`; a log-shipper 400 "entry too far behind"
  burst right after a reboot (duplicate re-ship, zero loss).

**Record** 📝 — paste each checkpoint's memory snapshot + any red lines into the step-8 row /
Compute notes, then the final verdict lands as a **Known deployments** row.

## 🤝 Contributing an entry

1. Run the stack (`./launch.sh`, or the `--pinned` appliance flow — see [DEPLOYMENT.md](DEPLOYMENT.md)).
2. Note CPU model, core count, RAM, storage, host OS (name + version), the GenWave version you
   ran (image tag, or "source" + commit), and which profiles (`--pinned`, `admin`, `logging`,
   `tunnel`, the demo LLM overlay).
3. PR a row into **Known deployments** with 🟢 for what you verified and a note for anything that
   needed tuning (e.g. a different ollama fence). Problems are as valuable as successes — file
   them as issues and reference them from a 🔴 row.
