#!/usr/bin/env bash
# Runs inside the GameCI Unity image (unityci/editor), project mounted at the working directory.
# Activates the licence, runs CiCheckRunner and copies reports to Artifacts/.
set -uo pipefail

UNITY="${UNITY_PATH:-/opt/unity}/Editor/Unity"
ARTIFACTS="Artifacts"
mkdir -p "$ARTIFACTS/temp" "$HOME/.cache/unity3d"

# Unity 6 licensing needs an account login to grant the Personal entitlement in batchmode.
LOGIN=()
if [ -n "${UNITY_EMAIL:-}" ] && [ -n "${UNITY_PASSWORD:-}" ]; then
  LOGIN=(-username "$UNITY_EMAIL" -password "$UNITY_PASSWORD")
else
  echo "UNITY_EMAIL/UNITY_PASSWORD not set: licence file only."
fi

if [ -n "${UNITY_SERIAL:-}" ]; then
  echo "Activating serial licence"
  "$UNITY" -batchmode -nographics -quit -logFile "$ARTIFACTS/activation.log" \
    -serial "$UNITY_SERIAL" -username "${UNITY_EMAIL:-}" -password "${UNITY_PASSWORD:-}" || true
elif [ -n "${UNITY_LICENSE:-}" ]; then
  echo "Activating licence file"
  LICENSE_DIR="$HOME/.local/share/unity3d/Unity"
  mkdir -p "$LICENSE_DIR"
  printf '%s' "$UNITY_LICENSE" > "$LICENSE_DIR/Unity_lic.ulf"
  "$UNITY" -batchmode -nographics -quit -logFile "$ARTIFACTS/activation.log" \
    -manualLicenseFile "$LICENSE_DIR/Unity_lic.ulf" "${LOGIN[@]}" || true
else
  echo "::error::Neither UNITY_LICENSE nor UNITY_SERIAL is set."
  exit 3
fi

ARGS=(-batchmode -projectPath "$PWD" -executeMethod MiningGame.CI.CiCheckRunner.Run
      -ciPlan "$ARTIFACTS/plan.json" -ciOutput "$ARTIFACTS" -logFile "$ARTIFACTS/editor.log" "${LOGIN[@]}")
if [ "${CI_NOGRAPHICS:-0}" != "1" ] && command -v xvfb-run >/dev/null; then
  xvfb-run -a -s "-screen 0 1920x1080x24" "$UNITY" "${ARGS[@]}"
else
  "$UNITY" -nographics "${ARGS[@]}"
fi
code=$?
echo "Unity exit code: $code"

if [ -n "${UNITY_SERIAL:-}" ]; then
  "$UNITY" -batchmode -nographics -quit -returnlicense -logFile "$ARTIFACTS/return.log" \
    -username "${UNITY_EMAIL:-}" -password "${UNITY_PASSWORD:-}" || true
fi

# Check reports and previews the scripts write to Temp/.
find Temp -maxdepth 1 -type f \( -name '*.txt' -o -name '*.json' -o -name '*.png' \) -exec cp {} "$ARTIFACTS/temp/" \; 2>/dev/null || true
exit $code
