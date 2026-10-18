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
UI_MODE="${CI_EDITOR_UI:-auto}"
if [ "$UI_MODE" = auto ]; then
  if grep -q '"mode": "play"' "$ARTIFACTS/plan.json"; then UI_MODE=1; else UI_MODE=0; fi
fi
ARGS=(-projectPath "$PWD" -executeMethod MiningGame.CI.CiCheckRunner.Run
      -ciPlan "$ARTIFACTS/plan.json" -ciOutput "$ARTIFACTS" -logFile "$ARTIFACTS/editor.log")
# Guard against modal dialogs or hangs: the runner's own timeouts cannot fire if the editor blocks.
LIMIT="${CI_UNITY_TIMEOUT:-100m}"
if [ "${CI_NOGRAPHICS:-0}" != "1" ] && command -v xvfb-run >/dev/null; then
  if [ "$UI_MODE" = 1 ]; then
    echo "Starting Unity with editor UI on virtual display"
    timeout "$LIMIT" xvfb-run -a -s "-screen 0 1920x1080x24" "$UNITY" "${ARGS[@]}"
  else
    timeout "$LIMIT" xvfb-run -a -s "-screen 0 1920x1080x24" "$UNITY" -batchmode "${ARGS[@]}"
  fi
else
  timeout "$LIMIT" "$UNITY" -batchmode -nographics "${ARGS[@]}"
fi
code=$?
echo "Unity exit code: $code"

# Free the seat again so it does not count as an extra machine.
if [ "$STRATEGY" = serial ]; then
  "$UNITY" -batchmode -nographics -quit -returnlicense -logFile "$ARTIFACTS/return.log" \
    -username "${UNITY_EMAIL:-}" -password "${UNITY_PASSWORD:-}" || true
elif [ "$STRATEGY" = personal ]; then
  "$CLIENT" --return-ulf > "$ARTIFACTS/return.log" 2>&1 || true
fi

# Check reports and previews the scripts write to Temp/.
find Temp -maxdepth 1 -type f \( -name '*.txt' -o -name '*.json' -o -name '*.csv' -o -name '*.png' \) -exec cp {} "$ARTIFACTS/temp/" \; 2>/dev/null || true
exit $code
