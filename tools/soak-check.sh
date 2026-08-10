#!/usr/bin/env bash
# tools/soak-check.sh — one-command soak checkpoint for a box under test (HARDWARE.md
# "Hands-on test plan" step 8 and its multi-day extension). Read-only: inspects, never changes.
#
# Run ON the box:            ./tools/soak-check.sh
# Or from a workstation:     ssh <box> 'bash -s' < tools/soak-check.sh
#
# Encodes the checks (and the traps) from the 2026-08 Pi 5 runs so a Pi 4 — or any future
# appliance — gets the IDENTICAL test:
#   • icecast's public status-json hides mounts BY DESIGN (F67 hardening) — "no source
#     connected" there is NOT an outage signal. Stream truth = metadata updates in icecast's
#     own log + an ESTABLISHED socket on :8000 (checked via /proc/net/tcp; the port is
#     deliberately not host-published, so never probe localhost:8000 from the host).
#   • Check /boot/firmware/config.txt for overclock lines FIRST — the 2026-08-02 Pi 5
#     undervoltage hunt was arm_freq=2900 all along; no measurement is trustworthy over one.
#   • Engine/liquidsoap log lines carry container-local time (UTC/BST skew has misled before);
#     every timestamp comparison here uses `docker logs -t` daemon-side UTC timestamps instead.
#   • `docker exec` runs INSIDE the target container's cgroup — near a mem_limit it can force
#     page-cache eviction and move the very numbers being read (observed live on alloy,
#     2026-08-09). The two tiny execs below (icecast awk) are negligible, but don't add fat ones.
set -uo pipefail

PROJECT="${SOAK_PROJECT:-genwave}"
FAIL=0
pass() { printf '  🟢 %s\n' "$1"; }
warn() { printf '  🟡 %s\n' "$1"; }
fail() { printf '  🔴 %s\n' "$1"; FAIL=1; }
info() { printf '     %s\n' "$1"; }
section() { printf '\n== %s ==\n' "$1"; }

cid_of() { docker ps -q --filter "label=com.docker.compose.project=$PROJECT" --filter "label=com.docker.compose.service=$1" | head -1; }

section "Host"
info "$(date -u '+%Y-%m-%dT%H:%M:%SZ') — $(uptime -p) — load $(cut -d' ' -f1-3 /proc/loadavg)"

section "Pi firmware (skipped on non-Pi)"
if command -v vcgencmd >/dev/null 2>&1; then
  throttled=$(vcgencmd get_throttled | cut -d= -f2)
  [ "$throttled" = "0x0" ] && pass "get_throttled=0x0" || fail "get_throttled=$throttled (any nonzero = under/over-volt or thermal event since boot)"
  info "$(vcgencmd measure_temp) — arm clock $(vcgencmd measure_clock arm | cut -d= -f2) Hz"
  if grep -qE '^(arm_freq|over_voltage)' /boot/firmware/config.txt 2>/dev/null; then
    fail "overclock lines present in config.txt — remove before trusting ANY soak measurement (Pi 5 lesson, 2026-08-02)"
  else
    pass "config.txt carries no overclock lines"
  fi
else
  info "vcgencmd not found — not a Pi, firmware checks skipped"
fi

section "Containers: restarts + OOM (criterion: all zero/false)"
bad=0
while read -r cid; do
  [ -n "$cid" ] || continue
  line=$(docker inspect "$cid" --format '{{.Name}} restarts={{.RestartCount}} oom={{.State.OOMKilled}} status={{.State.Status}}')
  info "$line"
  case "$line" in *"restarts=0 oom=false status=running"*) ;; *) bad=1 ;; esac
done < <(docker ps -aq --filter "label=com.docker.compose.project=$PROJECT")
[ "$bad" = 0 ] && pass "every container running, 0 restarts, no OOM kills" || fail "a container restarted, OOM-killed, or is not running — see lines above"

section "Memory snapshot (record these in HARDWARE.md; compare against the previous checkpoint)"
docker stats --no-stream --format '     {{.Name}} {{.CPUPerc}} {{.MemUsage}}' | sort
swap_used=$(free -m | awk '/^Swap:/ {print $3}')
[ "${swap_used:-0}" -eq 0 ] && pass "swap untouched" || warn "swap in use: ${swap_used}MiB — memory pressure worth explaining"
info "$(free -h | awk '/^Mem:/ {print "host mem: used " $3 " / " $2 ", available " $7}')"

section "Disk + media mount"
rootuse=$(df --output=pcent / | tail -1 | tr -dc '0-9')
[ "${rootuse:-100}" -lt 80 ] && pass "root disk at ${rootuse}%" || warn "root disk at ${rootuse}% — investigate growth (docker logs? tts cache?)"
mount | grep -qi nfs && info "NFS: $(mount | grep -i nfs | head -1 | cut -d' ' -f1-3)" || info "no NFS mount (fine if media is local)"

section "API"
code=$(curl -s -m 5 -o /tmp/soak-health.$$ -w '%{http_code}' localhost:8080/health 2>/dev/null || echo 000)
body=$(head -c 40 /tmp/soak-health.$$ 2>/dev/null; rm -f /tmp/soak-health.$$)
[ "$code" = "200" ] && pass "/health → 200 ${body}" || fail "/health → HTTP ${code} (expected 200; api loopback-published on :8080)"

section "Stream truth (NOT status-json — F67 hides mounts there by design)"
ICECAST=$(cid_of icecast)
if [ -n "$ICECAST" ]; then
  meta=$(docker logs -t --since 15m "$ICECAST" 2>&1 | grep -c 'admin/metadata' || true)
  [ "${meta:-0}" -gt 0 ] && pass "engine pushed metadata to icecast ${meta}x in the last 15 min" \
    || fail "no metadata updates in 15 min — the source feed is stalled or the station is silent"
  est=$(docker exec "$ICECAST" sh -c "awk 'NR>1 && \$4==\"01\"' /proc/net/tcp | grep -ci 1f40" 2>/dev/null || echo 0)
  [ "${est:-0}" -ge 1 ] && pass "${est} ESTABLISHED connection(s) on :8000 inside icecast" \
    || fail "no ESTABLISHED sockets on :8000 — nothing is feeding or listening"
else
  fail "icecast container not found for project '$PROJECT'"
fi

section "Safe-branch engagements (criterion: switch history = boot ladder only, nothing after T0+180s)"
ENGINE=$(cid_of engine)
if [ -n "$ENGINE" ]; then
  started=$(docker inspect "$ENGINE" --format '{{.State.StartedAt}}')
  cutoff=$(date -u -d "$started + 180 seconds" '+%Y-%m-%dT%H:%M:%S' 2>/dev/null || echo "")
  switches=$(docker logs -t "$ENGINE" 2>&1 | grep -F 'Switch to' || true)
  total=$(printf '%s' "$switches" | grep -c . || true)
  if [ -n "$cutoff" ]; then
    late=$(printf '%s\n' "$switches" | awk -v c="$cutoff" 'NF && $1 > c')
    # Only switches AWAY from the main queue are engagements; the paired switch back to
    # metadata_deduplicate is the recovery, counted separately so a lone return line can't
    # double a blip.
    engagements=$(printf '%s\n' "$late" | grep -cE 'Switch to (append|safe)' || true)
    info "switch lines total: ${total} (boot ladder is ~5); after T0+180s: $(printf '%s\n' "$late" | grep -c . || true) (${engagements} engagement(s))"
    if [ "${engagements:-0}" -eq 0 ]; then
      pass "zero mid-broadcast safe-branch engagements"
      [ -n "$late" ] && { info "post-boot switch lines (returns/other — eyeball):"; printf '%s\n' "$late" | tail -5; }
    else
      fail "safe branch engaged mid-broadcast ${engagements}x — lines follow"
      printf '%s\n' "$late" | tail -8
    fi
  else
    warn "could not compute boot cutoff — eyeball the ${total} switch lines manually"
  fi
else
  fail "engine container not found for project '$PROJECT'"
fi

section "Render pressure (last 24h; informational)"
API=$(cid_of api)
if [ -n "$API" ]; then
  maxhold=$(docker logs --since 24h "$API" 2>&1 | grep -oP 'Feeder refill held the tick for \K[0-9.]+' | sort -n | tail -1)
  info "max feeder refill hold: ${maxhold:-none logged}${maxhold:+s} (compare against Tts:RenderBudgetSeconds)"
  drops=$(docker logs --since 24h "$API" 2>&1 | grep -c 'render budget exceeded' || true)
  [ "${drops:-0}" -eq 0 ] && pass "zero render-budget drops in 24h" || warn "${drops} render-budget drop(s) in 24h — segments were silently skipped"
fi

section "Error-line counts, last 24h (informational — see HARDWARE.md for the known-benign list)"
while read -r cid; do
  [ -n "$cid" ] || continue
  name=$(docker inspect "$cid" --format '{{.Name}}' | tr -d /)
  n=$(docker logs --since 24h "$cid" 2>&1 | grep -ciE '(error|fatal)' || true)
  [ "${n:-0}" -gt 0 ] && info "$name: $n"
done < <(docker ps -q --filter "label=com.docker.compose.project=$PROJECT")
info "(known-benign: mjpeg EXIF decode on bad album art; wav max-data-size on TTS clips; rare one-off icecast metadata ECONNRESET)"

printf '\n'
[ "$FAIL" = 0 ] && printf '✅ SOAK CHECKPOINT PASS — record the memory snapshot above against the previous checkpoint.\n' \
               || printf '❌ SOAK CHECKPOINT FAIL — at least one criterion above is red.\n'
exit "$FAIL"
