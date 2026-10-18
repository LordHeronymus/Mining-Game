#!/usr/bin/env bash
# Claude-Code-Hook (.claude/settings.json): setzt fuer die eigene Sitzung automatisch ein Lebenszeichen
# in der Editor-Warteschlange, hoechstens einmal pro Minute. Ohne Eintrag der Sitzung passiert nichts.
# Doku: Docs/Editor-Warteschlange.md
input="$(cat)"
sid="$(printf '%s' "$input" | sed -n 's/.*"session_id"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1)"
[ -n "$sid" ] || exit 0

dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
qdir="$EDITOR_QUEUE_DIR"
if [ -z "$qdir" ]; then
    root="$(dirname "$dir")"
    owner="${root%%/.claude/worktrees/*}"
    qdir="$(dirname "$owner")/EditorQueue"
fi
[ -d "$qdir" ] || exit 0

for f in "$qdir"/*.json; do
    [ -f "$f" ] || continue
    case "$f" in *.activity.json) continue ;; esac
    grep -q "\"session\":[[:space:]]*\"$sid\"" "$f" || continue
    project="$(basename "$f" .json)"
    stamp="$qdir/.hook-$project-$sid"
    now="$(date +%s)"
    last="$(stat -c %Y "$stamp" 2>/dev/null || echo 0)"
    [ $((now - last)) -ge "${EDITOR_QUEUE_HOOK_SECONDS:-60}" ] || continue
    touch "$stamp"
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$dir/editor-queue.ps1")" \
        heartbeat -Project "$project" -Session "$sid" -QueueDir "$(cygpath -w "$qdir")" >/dev/null 2>&1
done
exit 0
