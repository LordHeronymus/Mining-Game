#!/usr/bin/env bash
# Publishes Artifacts/previews of a run to the orphan branch ci-previews as runs/<run number>/, so agents can look
# at the images without the artifact download (Tools/ci-previews.sh) and the dashboard can show them.
# The branch holds one commit with the latest KEEP runs; older runs are dropped.
set -euo pipefail
KEEP=15
work="$RUNNER_TEMP/ci-previews"
rm -rf "$work" && mkdir -p "$work"
git -C "$work" init -q
git -C "$work" remote add origin "$(git config --get remote.origin.url)"
# The checkout stores the token as an extra header; reuse it for this push.
header=$(git config --get http.https://github.com/.extraheader || true)
[ -n "$header" ] && git -C "$work" config http.https://github.com/.extraheader "$header"
if git -C "$work" fetch -q --depth 1 origin ci-previews 2>/dev/null; then git -C "$work" checkout -q FETCH_HEAD -- . ; fi
mkdir -p "$work/runs/$RUN"
cp -r Artifacts/previews/. "$work/runs/$RUN/"
python3 - "$work/runs/$RUN" <<PY
import json, os, sys, glob
root = sys.argv[1]
files = sorted(os.path.relpath(p, root) for p in glob.glob(root + "/**/*.png", recursive=True))
json.dump({"run": int(os.environ["RUN"]), "ref": os.environ.get("REF", ""), "sha": os.environ.get("GITHUB_SHA", ""),
           "suite": os.environ.get("CHECKS") or os.environ.get("SUITE", ""), "files": files},
          open(os.path.join(root, "index.json"), "w"), indent=1)
PY
ls -1 "$work/runs" | sort -n | head -n -$KEEP | while read -r old; do rm -rf "$work/runs/$old"; done
cd "$work"
git checkout -q --orphan publish
git add -A
git -c user.name="github-actions" -c user.email="github-actions@users.noreply.github.com" commit -qm "Previews of run #$RUN"
git push -q --force origin publish:ci-previews
echo "Previews: branch ci-previews, runs/$RUN ($(find "runs/$RUN" -name '*.png' | wc -l) images)"
