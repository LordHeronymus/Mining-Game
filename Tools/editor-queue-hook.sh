#!/usr/bin/env bash
# Claude-Code-Hook (.claude/settings.json, PreToolUse/PostToolUse): meldet der Editor-Warteschlange,
# wenn die eigene Sitzung den Unity Editor tatsaechlich benutzt. Nur Unity-Aufrufe zaehlen
# (Unity-MCP-Werkzeuge, `unity …`-Befehle, Tools/editor-bench): vorher "laeuft" (heartbeat -Busy),
# danach "beendet" (heartbeat -Idle). Code lesen/schreiben haelt den Editor nicht fest; wer zwischen
# Tests Code aendert, wird nach der kurzen Frist verdraengt. Ohne Eintrag der Sitzung passiert nichts.
# Doku: Docs/Editor-Warteschlange.md
input="$(cat)"
field() { printf '%s' "$input" | sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" | head -n 1; }
sid="$(field session_id)"
[ -n "$sid" ] || exit 0
event="$(field hook_event_name)"
tool="$(field tool_name)"

case "$tool" in
    mcp__*unity*) ;;
    Bash|PowerShell)
        cmd="$(field command)"
        printf '%s' "$cmd" | grep -qiE '(^|[^a-z-])unity(\.exe)?[[:space:]]+(command|cmd)|editor-bench' || exit 0
        ;;
    *) exit 0 ;;
esac
case "$event" in
    PreToolUse) flag=-Busy ;;
    PostToolUse|PostToolUseFailure) flag=-Idle ;;
    *) exit 0 ;;
esac

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
    case "$f" in *.activity.json|*.bench.json) continue ;; esac
    grep -q "\"session\":[[:space:]]*\"$sid\"" "$f" || continue
    project="$(basename "$f" .json)"
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$dir/editor-queue.ps1")" \
        heartbeat "$flag" -Project "$project" -Session "$sid" -QueueDir "$(cygpath -w "$qdir")" >/dev/null 2>&1
done
exit 0
