#!/usr/bin/env bash
# Fetches the preview images of a cloud run (branch ci-previews, see Docs/Cloud-Tests.md) into a local folder.
#   Tools/ci-previews.sh            list the runs with previews
#   Tools/ci-previews.sh 152 DIR    extract runs/152 into DIR (default: Temp/ci-previews/152)
set -euo pipefail
git fetch -q --depth 1 origin ci-previews
if [ $# -eq 0 ]; then
  git ls-tree --name-only FETCH_HEAD runs/ | sed 's|runs/||' | sort -n
  exit 0
fi
run=$1; out=${2:-Temp/ci-previews/$run}
mkdir -p "$out"
git archive FETCH_HEAD "runs/$run" | tar -x -C "$out" --strip-components=2
find "$out" -type f | sort
