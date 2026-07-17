# genwave.liq — single-station broadcast playout.
# The C# control plane pushes annotated requests over the telnet socket; this script applies
# per-track gain, crossfades, backstops dead air, and streams to Icecast.
# Target: Liquidsoap 2.4.x  (pinned to v2.4.4 in compose — operator/setting syntax shifts across versions).

# --- Control socket: bind 0.0.0.0 so the api container can reach it. Port 1234 is NEVER
#     published in compose, so "all interfaces" resolves to "the internal Docker network only".
#     The protocol is UNAUTHENTICATED — network isolation is the only guard. ---
settings.server.telnet := true
settings.server.telnet.bind_addr := "0.0.0.0"
settings.server.telnet.port := 1234

# --- Station identity for listeners (issue gitea-#148). STATION_NAME is compose-injected from the same
#     value as the API's Station__Name so the two can't drift. It must ride the in-band ICY
#     StreamTitle: radios that negotiate icy-metaint display StreamTitle and ignore the icy-name
#     header on the now-playing screen — and custom metadata fields (station_id/station_name above)
#     never reach ICY clients at all; the export list only serves our own telnet now-playing read. ---
station_name = environment.get("STATION_NAME", default="My GenWave Station")

# --- Energy-aware crossfade bounds (STORY-035). GW_XFADE_MIN / GW_XFADE_MAX define the
#     [min, max] fade-duration range. Hotter energy pairs → shorter fade (more punch);
#     mellower pairs → longer fade (smoother blend). xfade_max also drives the cross() buffer
#     window so the engine sees the full potential overlap. Defaults: 2s min, 8s max.
#     Defined here (before gw_transition) so the function closure captures the values. ---
xfade_min = float_of_string(default=2., environment.get("GW_XFADE_MIN"))
xfade_max = float_of_string(default=8., environment.get("GW_XFADE_MAX"))

# --- The queue the C# feeder pushes to. id="q" must match Liquidsoap__QueueId in the API config.
#     The feeder pushes with q.push <annotated uri> (returns a request id). "What is on air now" is
#     read from the OUTPUT metadata (output.icecast.metadata) below — NOT request.all/q.queue, which
#     are unreliable for on-air state (PRD §0). ---
q = request.queue(id="q")

# --- Per-track level matching. Base factor 1.0 (no blanket gain); `override` pulls each track's
#     precomputed value out of its `replay_gain` metadata (set by the feeder via annotate:).
#     Applied PER SOURCE here, BEFORE the crossfade — so each track keeps its own level through
#     the overlap instead of one gain smearing across both. ---
q = amplify(1., override="replay_gain", q)

# --- Trailing-silence trim (gitea-#151 pt 2). Many files end in seconds of near-digital silence; cross
#     buffers the LAST 6s of every track, so a silent outro puts dead air in front of every voice
#     segment no matter how the transition mixes (and cross plays the first
#     (window − next_track_len) seconds of the tail raw when the next track is shorter than the
#     window — see cross.ml length-matching). Eat any stretch quieter than -50dB after 1.5s so the
#     transition window always holds audible audio. Placed AFTER amplify: levels are normalized to
#     target loudness here, so the threshold means the same thing for every track. -50dB on a
#     -16 LUFS stream is deep silence — quiet musical passages are far above it. ---
q = blank.eat(threshold=-50., max_blank=1.5, q)

# --- Conditional transitions (issues gitea-#151/gitea-#155).
#     music → music : energy-aware crossfade (STORY-035). When both tracks carry
#       gw_outro_energy (outgoing) and gw_intro_energy (incoming) annotations, the fade
#       duration is computed as:
#           hot = (ea + eb) / 2.0          # average energy, range [0.0, 1.0]
#           d   = xfade_max - (xfade_max - xfade_min) * hot
#       Monotonically hotter → shorter fade (more impact); mellower → longer (smoother blend).
#       d is already clamped to [xfade_min, xfade_max] by construction when energies are in
#       [0.0, 1.0]. When EITHER annotation is absent or unparseable (sentinel -1.), the branch
#       falls back to a fixed 3-second fade — the same safe default as before STORY-035.
#     music → voice : overlay + duck (gitea-#151 pt 2). A butt splice here waits out the song's own
#       quiet outro before the voice starts — audible dead air. Instead the voice starts the
#       moment the cross window opens, mixed over the song's tail ducked to ~30% and fading out;
#       the "duck" releases naturally when the tail ends. b.source MUST be first in the add list:
#       add relays metadata from the first source only, and the feeder's chain tracking needs the
#       TTS track_id to reach the output metadata.
#     voice → *     : butt splice. Voice clips are short and tight — fades on a voice sound
#       wrong, and the next item starting clean right after speech is correct.
#     cross() duration= must equal xfade_max so the buffer window covers the widest possible
#     overlap; smoke_test.sh's CROSSFADE default must match. ---
def gw_transition(a, b) =
  if b.metadata["gw_tts"] == "true" and a.metadata["gw_tts"] != "true" then
    # The 0.1s fade.in is load-bearing twice over: it softens the voice onset AND keeps both
    # add() elements fade-wrapped — a bare b.source would unify with fade.out's methodful
    # return type and demand methods cross's parts don't have (type error).
    add(normalize=false,
        [fade.in(duration=0.1, b.source),
         amplify(0.3, fade.out(duration=6., a.source))])
  elsif a.metadata["gw_tts"] == "true" then
    sequence([a.source, b.source])
  else
    # Energy-aware music→music fade. Use sentinel -1. to detect absent/unparseable annotations.
    ea = float_of_string(default=(-1.), a.metadata["gw_outro_energy"])
    eb = float_of_string(default=(-1.), b.metadata["gw_intro_energy"])
    if ea >= 0. and eb >= 0. then
      hot = (ea + eb) / 2.
      d   = xfade_max - ((xfade_max - xfade_min) * hot)
      cross.simple(fade_in=d, fade_out=d, a.source, b.source)
    else
      # One or both energy annotations missing — fixed safe fallback.
      cross.simple(fade_in=3., fade_out=3., a.source, b.source)
    end
  end
end
main = cross(duration=xfade_max, gw_transition, q)

# --- Dead-air backstop: a curated library scope served by GET /internal/safe-track (F21).
#     request.dynamic fetches an annotated URI on every call — same loudness, cue, and energy
#     annotations as main-rotation pushes. Empty response (204 / no body) produces default=""
#     → request.create("") fails → mksafe engages (F4.4). track_sensitive=false cuts to it the
#     instant the queue drains, and back the instant real content returns.
#     prefetch=1 caps safe_lib at one pre-resolved request — the STORY-061 spike's "adopt ≤1"
#     verdict (F22.1, MEMORY.md 2026-07-02) — so a scope/eligibility edit is reflected within
#     at most one stale prefetched track (F22.4), never more.
#     retry_delay=5. backs off the resolver while the api is unreachable (startup window,
#     `api` restart) instead of spinning at the 0.1s default, which is what caused the
#     "possible request leak" RID churn (F22.3). ---
safe = request.dynamic(id="safe_lib", prefetch=1, retry_delay=5., { request.create(list.hd(default="", process.read.lines("curl -s http://api:8080/internal/safe-track"))) })

# --- Per-track level matching on the safe branch (F37.1, gitea-#200). Mirrors the main queue's amplify
#     above verbatim — safe plays were airing un-level-matched despite carrying a real stamped gain,
#     contradicting F21.4's "loudness matching … all apply". Placed BEFORE the F29.6 gap append below
#     so the blank gap itself carries no gain (only real content has a replay_gain annotation). ---
safe = amplify(1., override="replay_gain", safe)

# --- Inter-safe-track gap (F29.6, STORY-099, gitea-#182 — R4 `--check` spike, MEMORY.md 2026-07-11).
#     GW_SAFE_GAP_SECONDS seconds of silence follow every safe track before the next one starts,
#     so a drain doesn't read as an accidental medley — the gap is a property of the ROTATION,
#     not the content (rejected: authoring-time tail padding baked into the file — only authored
#     tracks would get it, and cue-out trimming would eat it again).
#     append() attaches a blank(duration=gap) track directly onto the safe branch, right after
#     every safe track — shadow-rebinding `safe` the same way `q` is progressively rebuilt
#     above. The gap is then a genuine track ON safe, so the outer
#     fallback(track_sensitive=false, ...) below preempts it — and cuts back to main — the
#     instant main is ready, exactly like it preempts any other safe track; no new readiness
#     edge case for cutback latency.
#     Rejected: delay(gap, safe) — the more "native"-looking operator (make the source unready
#     between tracks; let mksafe fill the silence). Two reasons against: (1) mksafe's own
#     comment below promises silence there is "the only path... and it requires an ops error"
#     — routing the deliberate gap through mksafe's leaf makes that false on every ordinary
#     drain, not just failures; (2) the spike could not confirm on `--check` alone whether the
#     underlying request.dynamic's prefetch=1 queue keeps resolving while delay holds its
#     wrapper deliberately unready — an assumption append needs not make.
#     GW_SAFE_GAP_SECONDS=0 disables the gap outright: `safe` passes through unwrapped, the
#     exact pre-R4 graph shape. ---
gw_safe_gap_seconds = float_of_string(default=7., environment.get("GW_SAFE_GAP_SECONDS"))
safe =
  if gw_safe_gap_seconds > 0. then
    append(safe, fun (_) -> blank(duration=gw_safe_gap_seconds))
  else
    safe
  end
genwave = fallback(track_sensitive=false, [main, safe])

# --- Final guard: if even the safe file is missing/corrupt, emit silence rather than crash.
#     The only path to silence in the whole system, and it requires an ops error. ---
genwave = mksafe(genwave)

# --- Export our stamped custom fields + on_air onto the output metadata (PRD §0).
#     output.<id>.metadata is FILTERED to this list: standard tags (title, artist, …) are exported
#     by default, but custom annotate fields (track_id, pos) do NOT appear unless added here — which
#     is why the C# feeder reads "what's on air" from the output (track_id/on_air), never from the
#     unreliable request-level state. APPEND so the defaults (title for the smoke test, etc.) survive.
#     "replay_gain" joins the list so engine-initiated plays' frames carry the stamped gain (F37.2,
#     gitea-#200) — the feeder's shipped ExtractAnnotations parses it already; the key just never arrived.
settings.encoder.metadata.export :=
  list.append(settings.encoder.metadata.export(),
              ["track_id", "station_id", "station_name", "on_air", "on_air_timestamp", "replay_gain"])

# Build StreamTitle as "<station> | <artist> - <title>", degrading gracefully when tags are missing
# (mirrors the icy_song default of artist/title/null, with the station prefixed).
def gw_icy_song(m) =
  artist = m["artist"]
  title  = m["title"]
  base =
    # TTS patter carries the station name as its generic title (gitea-#148/gitea-#154); when the
    # artist is also known (persona name, or the station itself pre-persona), the title
    # is redundant — collapse to the artist alone so the final guard below can turn a
    # persona-less patter's "station - station" into plain "station", not a duplicate
    # (F39.5/gitea-#212, display-only, off the metadata-event/track_id path).
    if artist != "" and title == station_name then artist
    elsif artist != "" and title != "" then "#{artist} - #{title}"
    elsif title != "" then title
    else artist
    end
  # TTS segments carry the station name as their generic title (gitea-#148/gitea-#154) — avoid "X | X".
  if base == "" or base == station_name then station_name
  else "#{station_name} | #{base}" end
end

# --- Broadcast. Source password and host from env (compose injects them).
#     ICECAST_HOST defaults to "icecast" — one deployment broadcasts one station; a second
#     station is a second deployment that sets its own ICECAST_HOST if it needs to. ---
output.icecast(
  %mp3(bitrate=192),
  host=environment.get("ICECAST_HOST", default="icecast"),
  port=int_of_string(environment.get("ICECAST_PORT", default="8000")),
  mount="stream",
  password=environment.get("ICECAST_SOURCE_PASSWORD"),
  name=station_name, description=station_name,
  icy_song=gw_icy_song,
  genwave
)
