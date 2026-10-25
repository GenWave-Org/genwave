# 🖥️ Hardware

What GenWave runs on, what each service needs, and how sure we are of each claim.
Running it on something else? Add a row to Known deployments with a pull request.

## 🎨 Confidence legend

| Mark | Meaning |
|:---:|---|
| 🟢 | **Verified.** GenWave has run here, or the number was measured live |
| 🟡 | **Expected.** Worked out from configured limits or design; not measured |
| 🔴 | **Unverified or known to be a problem** |

## 📦 Known deployments

| Machine | Arch | Cores | RAM | Storage | OS | GenWave | Role | Status | Notes |
|---|---|:---:|:---:|:---:|---|:---:|---|:---:|---|
| Development machine (Dell Precision 7920 Tower) | x86-64 | 112 | 512GB | 4TB | Ubuntu 25.04 | source (`main`) | `./launch.sh`, full stack from source | 🟢 | Docker |
| Raspberry Pi 5 Model B Rev 1.0 | **arm64** | 4 | **4GB** | 256GB NVMe via M.2 HAT+, no SD card | Debian 13 trixie | `home-v2.9.1` (last verified) | Piper-only playout, `./launch.sh --pinned --piper-only` | 🟢 | **First ARM deployment** (2026-08-02). Gapless stream; 66–69 °C under a full enrichment burst; `vcgencmd get_throttled` = `0x0` at 2.4 GHz. Under 4-core enrichment load: piper **RTF 0.252**, enrichment **~800 tracks/h**, **zero mid-broadcast safe-branch engagements** in 1 h 40 m. 9,094-track library over NFS. Kernel 6.12 (16k pages). **Three hard prerequisites.** (1) Stock CPU clock: `grep -E "arm_freq\|over_voltage\|force_turbo" /boot/firmware/config.txt` must print nothing; an overclocked test box undervolted every two minutes and hit 83 °C. (2) Memory cgroup: Pi kernels ship it off and Docker silently discards every `mem_limit`; append `cgroup_enable=memory cgroup_memory=1` to `/boot/firmware/cmdline.txt` (one line), reboot, then `docker compose down` and `./launch.sh --pinned --piper-only` so containers are recreated with the limits; check `docker stats --no-stream` shows piper at `/ 768MiB`, not total RAM. (3) The official 27 W (5 V/5 A) Raspberry Pi PSU; third-party supplies sagged under load, and with an NVMe HAT this is required. Also fit an active cooler: the Pi soft-limits at 80 °C. Check a box with `vcgencmd get_throttled` after a load burst (want `0x0`) |
| Raspberry Pi 4 Model B Rev 1.1 | **arm64** | 4 (A72, 1.5 GHz) | **4GB** | **16GB SD card** (ext4) | Debian 12 bookworm | `home-v5.2.1` | Piper-only playout, `./launch.sh --pinned --piper-only` | 🟢 | **7-day soak passed** (2026-08-09 to 08-17, `home-v3.3.2`, 7 d 10 h 51 m): `get_throttled=0x0` throughout, **0 restarts and 0 OOM kills** on all 8 containers, no api warnings or errors in any 24 h window, swap untouched. Engine plateaued near 270 MiB, api near 255 MiB; piper sat at ~405–411 MiB for six days then stepped to ~498 MiB (real heap, flat on re-sample, 768 MiB cap holds). Same 9,094-track library over NFS. Load ~0.7 idle, ~1.6 mid-decode. Upgraded to `home-v5.2.1` after the soak, airing verified the same day. The piper step is a watch item, not a fail. Same stock-clock check and the same cgroup fix as the Pi 5 row. SD cards make a power cut riskier than NVMe does |

### Internet radios

| Make | Model | Status | Notes |
|---|---|:---:|---|
| Grace Digital | Mondo Elite Classic | 🟢 | Works great! |

## 📐 Sizing at a glance

| Shape | RAM | Status | Notes |
|---|:---:|:---:|---|
| **Playout + piper voice**, no kokoro, no LLM | **4 GB** | 🟢 | Measured on a 4 GB Pi 5 with the whole stack resident. Enrichment is the peak and stays under ~500 MiB |
| **+ kokoro**, no LLM | 8 GB | 🟡 | Kokoro's ~1.2 GiB baseline is the biggest single resident. 4 GB is too tight |
| **+ LLM on the same box** | 8 GB min, 16 GB comfortable | 🟡 | The 6 GB ollama fence plus kokoro crowds an 8 GB box. Any model in the table below is truthful; size it to the fence |
| **Playout on a Pi, voice and LLM on another box** | 4 GB (Pi) | 🟡 | `Tts:Endpoint`, `Tts:Fallback:Endpoint`, `Llm:Endpoint` and `Llm:Model` can all be set live; never run in this split |
| **Pi with no LLM** | 4 GB | 🟢 | Patter uses the template writer. Mood tags and explicit flags are never filled in, and unflagged tracks stay in rotation |
| **CPU** | 4 cores | 🟢 | Enrichment, not playout, uses every core (`Library:EnrichmentConcurrency`, default 4; 2 under `--piper-only`) |
| **Disk** | your library | 🟢 | Music is mounted read-only; named volumes (Postgres, TTS segments, Piper models) are modest |
| **First catalog build** | ~11 to 13 h per 9,000 tracks | 🟢 | ~800 tracks/h at concurrency 4, ~700 at 2. Runs in the background while the station broadcasts. Raise `LIBRARY_ENRICHMENT_CONCURRENCY` to shorten it |

## 🧩 What each service needs

Limits come from `compose.yaml`, except the ollama fence, which was measured on a live box and is not a shipped default. On Raspberry Pi kernels they do nothing until the memory cgroup is on (see the Pi rows).

| Service | Limit | Real footprint | Status | Notes |
|---|:---:|---|:---:|---|
| `kokoro` (voice) | 4 GB cap | ~1.2 GiB fresh; grows toward the cap under render load | 🟢 | A 3 GB cap OOM-bounced every ~24–30 h; 4 GB buys headroom |
| `ollama` (DJ brain) | 1 CPU / 6 GB fence | needs over 3 GB with `llama3.2:3b` resident | 🟢 | A 3 GB fence was killed constantly. A full prompt on one fenced core takes ~25–30 s warm, so set `Llm:TimeoutSeconds: 60` |
| `piper` (fallback voice) | 768 MB cap | ~165–220 MiB | 🟢 | `en_US-lessac-medium`, downloaded on first boot. Measured on x86-64 and arm64 |
| `api` (with enrichment) | none | ~60 MiB idle; 400–500 MiB during enrichment | 🟢 | Transient peaks near 1 GiB are garbage-collection lag, not a floor |
| `cloudflared` (tunnel profile) | 128 MB cap | ~20–30 MiB | 🟢 | |
| `alloy` (logging profile) | 256 MB cap | not measured | 🟡 | Log-tailing sidecar |
| `dockerproxy` | 64 MB cap | not measured | 🟡 | Read-only docker-socket proxy for the Health page |
| `db` / `icecast` / `engine` / `admin_ui` | none | 5–130 MB | 🟡 | None has ever been the memory pressure point |

## ✅ Software requirements

| Requirement | Value | Status |
|---|---|:---:|
| OS | Linux with Docker Engine | 🟢 |
| Docker Compose | **v2.24 or newer** (the compose files use the `!override` merge tag) | 🟢 |
| Architecture | x86-64 or arm64 (images are multi-arch since `home-v2.8.8`) | 🟢 |
| Raspberry Pi kernel | `cgroup_enable=memory cgroup_memory=1` in `cmdline.txt` | 🟢 |
| GPU | None; nothing uses one | 🟢 |

## 🧠 LLM model table

Share of copy calls where the truth check rejected the DJ's text, on an RTX 4090 (so this measures truthfulness, not capacity).

| Model | Copy calls | Rejects | Share | Status |
|---|---:|---:|---:|:---:|
| `llama3.1:8b` | ~1,137 | 0 | 0.0% | 🟢 |
| `llama3.2:3b` | ~2,365 | 7 | 0.30% | 🟢 |
| `gemma3:12b` | ~2,152 | 0 | 0.0% | 🟢 |
| `mistral-nemo:12b` | ~1,103 | 3 | 0.27% | 🟢 |
| `mistral:7b` | ~1,001 | 3 | 0.30% | 🟢 |
| `gemma4:12b` | 15 | 15 empty | n/a | 🔴 Thinking models returned empty text on an older build; fixed since |

## 🤝 Contributing an entry

1. Run the stack (`./launch.sh`, or the `--pinned` flow in [DEPLOYMENT.md](DEPLOYMENT.md)).
2. Note CPU model, cores, RAM, storage, OS and version, the GenWave version (image tag, or "source" and commit), and the profiles you used.
3. Add a row to Known deployments: 🟢 for what you verified, and a note for anything you had to tune. Problems are as useful as successes.
