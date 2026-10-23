#!/usr/bin/env bash
# Runs inside the GameCI Unity image (unityci/editor), project mounted at the working directory.
# Activates the licence, runs CiCheckRunner, returns the licence and copies reports to Artifacts/.
#
# Licence strategies (same order as GameCI's activate.sh):
#   UNITY_SERIAL + email/password  Pro/Plus seat
#   UNITY_EMAIL + UNITY_PASSWORD   Personal seat through the Unity licensing client
#   UNITY_LICENSE only             legacy .ulf (Unity no longer issues these for Personal seats)
set -uo pipefail

UNITY="${UNITY_PATH:-/opt/unity}/Editor/Unity"
CLIENT="${UNITY_PATH:-/opt/unity}/Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client"
ARTIFACTS="Artifacts"
mkdir -p "$ARTIFACTS/temp" "$HOME/.cache/unity3d"
STRATEGY=""

# Hard time limit: the workflow passes CI_DEADLINE (epoch seconds, job start + 16 min; the job itself stops at 20 min).
# Every Unity/player call gets only the time that is left. When it runs out, abort() writes Artifacts/abort-reason.txt,
# which the report, the live status and the dashboard show, so agents see why and do not simply start the run again.
DEADLINE="${CI_DEADLINE:-$(( $(date +%s) + 100 * 60 ))}"
left() { local s=$(( DEADLINE - $(date +%s) )); [ $s -lt 30 ] && s=30; echo "${s}s"; }
abort() {
  local text="Abgebrochen: Zeitlimit von 20 Minuten erreicht ($1). Dieser Lauf ist für die Cloud zu aufwendig. Nicht erneut starten: Check verkleinern, mit // CI: local-only markieren oder lokal im Editor ausführen."
  echo "$text" > "$ARTIFACTS/abort-reason.txt"
  echo "::error title=Zeitlimit 20 min::$text"
}

if [ -n "${UNITY_SERIAL:-}" ]; then
  STRATEGY=serial
  echo "Activating serial licence"
  "$UNITY" -batchmode -nographics -quit -logFile "$ARTIFACTS/activation.log" \
    -serial "$UNITY_SERIAL" -username "${UNITY_EMAIL:-}" -password "${UNITY_PASSWORD:-}" || true
elif [ -n "${UNITY_EMAIL:-}" ] && [ -n "${UNITY_PASSWORD:-}" ]; then
  STRATEGY=personal
  echo "Activating Personal licence through the Unity account"
  FLAGS=(--activate-all)
  if "$CLIENT" --help 2>&1 | grep -q -- '--include-personal'; then FLAGS+=(--include-personal); fi
  ok=0
  for attempt in 1 2 3; do
    if "$CLIENT" "${FLAGS[@]}" --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD" > "$ARTIFACTS/activation.log" 2>&1; then
      ok=1; break
    fi
    echo "Activation attempt $attempt failed, retrying"; sleep $((attempt * 10))
  done
  # Mask the address before showing the log.
  sed "s/${UNITY_EMAIL//\//\\/}/***/g" "$ARTIFACTS/activation.log" | tail -n 30
  if [ "$ok" != 1 ] || grep -qiE "No seat available|No license activation found" "$ARTIFACTS/activation.log"; then
    echo "::error::Personal licence activation failed. Check the Unity account (id.unity.com) and the secrets UNITY_EMAIL/UNITY_PASSWORD."
    exit 4
  fi
elif [ -n "${UNITY_LICENSE:-}" ]; then
  STRATEGY=file
  echo "Activating licence file"
  printf '%s' "$UNITY_LICENSE" | tr -d '\r' > /tmp/UnityLicenseFile.ulf
  "$UNITY" -batchmode -nographics -quit -logFile "$ARTIFACTS/activation.log" \
    -manualLicenseFile /tmp/UnityLicenseFile.ulf || true
else
  echo "::error::No Unity licence configured (UNITY_EMAIL + UNITY_PASSWORD, UNITY_SERIAL or UNITY_LICENSE)."
  exit 3
fi

# Play-Mode checks need the full editor UI on the virtual display: in -batchmode the Game view
# never renders, so coroutines waiting for WaitForEndOfFrame (fades, loading screen) never resume.
# CI_EDITOR_UI: auto (play checks in the plan), 1 (always), 0 (never).
# Off by default: without Unity Hub sign-in the editor UI quits right after licensing
# ("Access token is unavailable", runs #8-#12). Performance measurements use the benchmark player instead.
UI_MODE="${CI_EDITOR_UI:-0}"
[ "$UI_MODE" = auto ] && UI_MODE=0
start_display() {
  # Mesa provides software OpenGL (GLX) on Xvfb for the editor UI or the benchmark player.
  (apt-get -qq update && apt-get -qq install -y --no-install-recommends \
      xdotool imagemagick mesa-utils libgl1-mesa-dri libglx-mesa0 libegl-mesa0) > "$ARTIFACTS/apt.log" 2>&1 \
    || echo "X tools not installed (see apt.log)"
  Xvfb :99 -screen 0 "${XVFB_SCREEN:-1920x1080x24}" +extension GLX +render -noreset > "$ARTIFACTS/xvfb.log" 2>&1 &
  XVFB_PID=$!
  export DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1
  sleep 2
  echo "--- OpenGL auf dem virtuellen Bildschirm"
  (glxinfo -B 2>&1 || echo "glxinfo nicht verfügbar") | head -n 12
}

return_licence() {
  if [ "$STRATEGY" = serial ]; then
    "$UNITY" -batchmode -nographics -quit -returnlicense -logFile "$ARTIFACTS/return.log" \
      -username "${UNITY_EMAIL:-}" -password "${UNITY_PASSWORD:-}" || true
  elif [ "$STRATEGY" = personal ]; then
    "$CLIENT" --return-ulf > "$ARTIFACTS/return.log" 2>&1 || true
  fi
}

# Suite "performance": build a Linux benchmark player (MINING_BENCHMARK, PerformanceBenchmarkDriver) and run it on
# the virtual display. A player has no editor dialogs and renders every frame, unlike the batchmode editor.
# Software rendering is slow (run #13: ~1.5 s per frame at 1280x720 Ultra, loading hit the driver's 300 s limit),
# so the player runs at a tiny resolution and the lowest quality level: the benchmark targets CPU work.
# Run #14 still spent ~100 ms per frame on software rendering, -nographics crashes the player (run #15), and with
# rendering the full run took 41 min (run #16, ~1.3 s per frame in the game). So the player keeps its graphics device but
# its cameras draw nothing (-benchmarkNoRender): frame times measure CPU work only. CI_PLAYER_RENDER=1 draws again.
if grep -q '"mode": "player"' "$ARTIFACTS/plan.json"; then
  echo "Building benchmark player (Linux, Mono)"
  timeout "$(left)" "$UNITY" -batchmode -nographics -projectPath "$PWD" \
    -executeMethod MiningGame.CI.CiBenchmarkBuild.Build -ciBuildPath "$ARTIFACTS/player" -logFile "$ARTIFACTS/editor.log"
  code=$?
  echo "Build exit code: $code"
  [ $code -eq 124 ] && abort "beim Bauen des Benchmark-Players"
  return_licence
  if [ $code -eq 0 ]; then
    XVFB_SCREEN="${CI_PLAYER_SCREEN:-640x360x24}" start_display
    PLAYER="$ARTIFACTS/player/TiefenhallBenchmark.x86_64"
    chmod +x "$PLAYER"
    started=$(date +%s)
    timeout "$(left)" "$PLAYER" -performanceBenchmark "$PWD/$ARTIFACTS/PerformanceBenchmark.txt" \
      -benchmarkLoadLimit "${CI_PLAYER_LOAD_LIMIT:-480}" $([ "${CI_PLAYER_RENDER:-0}" = 1 ] || echo -benchmarkNoRender) \
      -logFile "$PWD/$ARTIFACTS/player.log" -screen-fullscreen 0 \
      -screen-width "${CI_PLAYER_WIDTH:-320}" -screen-height "${CI_PLAYER_HEIGHT:-180}" -screen-quality "Very Low" \
      -force-glcore
    pcode=$?
    echo "Player exit code: $pcode"
    [ $pcode -eq 124 ] && abort "während des Benchmarks im Player"
    echo "$(( $(date +%s) - started )) $pcode" > "$ARTIFACTS/player-meta.txt"
    kill $XVFB_PID 2>/dev/null
    echo "--- Player-Log (Ende)"; tail -n 40 "$ARTIFACTS/player.log" 2>/dev/null
    [ -f "$ARTIFACTS/PerformanceBenchmark.txt" ] && cat "$ARTIFACTS/PerformanceBenchmark.txt"
    code=$pcode
  fi
  exit $code
fi

# Images written during the run (Temp/, Design/) become previews; the workflow publishes them (Docs/Cloud-Tests.md).
touch "$ARTIFACTS/.started"
ARGS=(-projectPath "$PWD" -executeMethod MiningGame.CI.CiCheckRunner.Run
      -ciPlan "$ARTIFACTS/plan.json" -ciOutput "$ARTIFACTS" -logFile "$ARTIFACTS/editor.log")
# Guard against modal dialogs or hangs: the runner's own timeouts cannot fire if the editor blocks.
LIMIT="$(left)"

# Screenshots and window titles of the virtual display, plus a cautious dialog handler: when the
# editor log has not grown for a while, the frontmost window that is not the main editor window
# gets Return (default button). Everything is logged to Artifacts/ for diagnosis.
watch_display() {
  local pid=$1 n=0 last_size=-1 idle=0
  mkdir -p "$ARTIFACTS/screens"
  while kill -0 "$pid" 2>/dev/null; do
    sleep 15; n=$((n + 1))
    if [ $n -le 8 ] || [ $((n % 8)) -eq 0 ]; then
      command -v import >/dev/null && import -window root "$ARTIFACTS/screens/$(printf %04d $((n * 15)))s.png" 2>/dev/null
    fi
    command -v xdotool >/dev/null || continue
    titles=$(for w in $(xdotool search --onlyvisible --name '.' 2>/dev/null); do echo "$w $(xdotool getwindowname "$w" 2>/dev/null)"; done)
    echo "== $(date +%T)" >> "$ARTIFACTS/windows.log"; echo "$titles" >> "$ARTIFACTS/windows.log"
    size=$(stat -c %s "$ARTIFACTS/editor.log" 2>/dev/null || echo 0)
    if [ "$size" = "$last_size" ]; then idle=$((idle + 15)); else idle=0; last_size=$size; fi
    if [ $idle -ge 90 ]; then
      dialog=$(echo "$titles" | grep -v -E ' - Unity [0-9]|^$' | tail -n 1 | cut -d' ' -f1)
      if [ -n "$dialog" ]; then
        echo "$(date +%T) editor log idle ${idle}s, pressing Return on window $dialog" | tee -a "$ARTIFACTS/windows.log"
        command -v import >/dev/null && import -window root "$ARTIFACTS/screens/dialog-$(date +%H%M%S).png" 2>/dev/null
        xdotool windowactivate --sync "$dialog" key --clearmodifiers Return 2>/dev/null
        idle=0
      fi
    fi
  done
}

if [ "${CI_NOGRAPHICS:-0}" != "1" ] && command -v Xvfb >/dev/null; then
  if [ "$UI_MODE" = 1 ]; then
    echo "Starting Unity with editor UI on virtual display"
    start_display
    # -automated: no human at the editor, dialogs are skipped or answered with their default.
    # Without -batchmode the editor needs a signed-in account (access token); without it, it shows a
    # sign-in dialog and quits right after "Pro License" (runs #8/#10/#11). Log in like the batch run #4 did.
    LOGIN=()
    if [ -n "${UNITY_EMAIL:-}" ] && [ -n "${UNITY_PASSWORD:-}" ]; then LOGIN=(-username "$UNITY_EMAIL" -password "$UNITY_PASSWORD"); fi
    timeout "$LIMIT" "$UNITY" -automated -force-glcore "${LOGIN[@]}" "${ARGS[@]}" &
    UNITY_PID=$!
    watch_display $UNITY_PID &
    wait $UNITY_PID
    code=$?
    kill $XVFB_PID 2>/dev/null
  else
    timeout "$LIMIT" xvfb-run -a -s "-screen 0 1920x1080x24" "$UNITY" -batchmode "${ARGS[@]}"
    code=$?
  fi
else
  timeout "$LIMIT" "$UNITY" -batchmode -nographics "${ARGS[@]}"
  code=$?
fi
echo "Unity exit code: $code"
[ $code -eq 124 ] && abort "während der Checks im Editor"
if [ -f "$ARTIFACTS/windows.log" ]; then echo "--- Fenster auf dem virtuellen Bildschirm (letzte Einträge)"; tail -n 40 "$ARTIFACTS/windows.log"; fi

# Free the seat again so it does not count as an extra machine.
return_licence

# Check reports and previews the scripts write to Temp/.
find Temp -maxdepth 1 -type f \( -name '*.txt' -o -name '*.json' -o -name '*.csv' -o -name '*.png' \) -exec cp {} "$ARTIFACTS/temp/" \; 2>/dev/null || true
# CiCheckRunner already collects them before Unity quits (it deletes Temp/); this is the fallback if it did not.
[ -d "$ARTIFACTS/previews" ] || find Temp Design -type f -name '*.png' -newer "$ARTIFACTS/.started" 2>/dev/null | head -n 60 | while read -r image; do
  mkdir -p "$ARTIFACTS/previews/$(dirname "$image")" && cp "$image" "$ARTIFACTS/previews/$image"
done
exit $code
