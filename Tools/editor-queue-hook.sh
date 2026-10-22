#!/usr/bin/env bash
# Claude-Code-Hook (.claude/settings.json, PreToolUse/PostToolUse) fuer die Editor-Warteschlange.
# 1. Lebenszeichen nur bei echter Editor-Nutzung: Unity-Aufrufe (Unity-MCP-Werkzeuge, `unity …`-Befehle,
#    Tools/editor-bench) melden vorher "laeuft" (heartbeat -Busy) und danach "beendet" (heartbeat -Idle).
#    Code lesen/schreiben haelt den Editor nicht fest.
# 2. Hinweise an den Chat (PostToolUse, hoechstens einmal pro Minute): Bittet der Benutzer um Freigabe
#    ("Bitte abschliessen") oder wurde die Sitzung verdraengt, bekommt Claude den Text als Zusatzinfo.
# Ohne Eintrag der Sitzung passiert nichts. Doku: Docs/Editor-Warteschlange.md
input="$(cat)"
field() { printf '%s' "$input" | sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" | head -n 1; }
sid="$(field session_id)"
[ -n "$sid" ] || exit 0
event="$(field hook_event_name)"
tool="$(field tool_name)"

unity=0
case "$tool" in
    mcp__*unity*) unity=1 ;;
    Bash|PowerShell)
        cmd="$(field command)"
        if printf '%s' "$cmd" | grep -qiE '(^|[^a-z-])unity(\.exe)?[[:space:]]+(command|cmd)|editor-bench'; then unity=1; fi
        ;;
esac
case "$event" in
    PreToolUse) [ "$unity" = 1 ] || exit 0 ;;
    PostToolUse|PostToolUseFailure) ;;
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
script="$(cygpath -w "$dir/editor-queue.ps1")"
qwin="$(cygpath -w "$qdir")"
q() { powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$script" "$@" -QueueDir "$qwin" 2>/dev/null | tr -d '\r'; }

notes=""
for f in "$qdir"/*.json; do
    [ -f "$f" ] || continue
    case "$f" in *.activity.json|*.bench.json|*.evicted.json) continue ;; esac
    project="$(basename "$f" .json)"
    inqueue=0
    grep -q "\"session\":[[:space:]]*\"$sid\"" "$f" && inqueue=1
    evicted=0
    ev="$qdir/$project.evicted.json"
    if [ -f "$ev" ] && grep -q "\"session\":[[:space:]]*\"$sid\"" "$ev" && grep -q '"notified":[[:space:]]*false' "$ev"; then evicted=1; fi
    [ "$inqueue" = 1 ] || [ "$evicted" = 1 ] || continue

    if [ "$event" = PreToolUse ]; then
        [ "$inqueue" = 1 ] && q heartbeat -Busy -Project "$project" -Session "$sid" >/dev/null
        continue
    fi

    # PostToolUse(Failure): Unity-Aufruf beendet melden, Hinweise hoechstens einmal pro Minute weitergeben.
    stamp="$qdir/.notice-$project-$sid"
    now="$(date +%s)"
    last="$(stat -c %Y "$stamp" 2>/dev/null || echo 0)"
    due=0
    [ $((now - last)) -ge "${EDITOR_QUEUE_NOTICE_SECONDS:-60}" ] && due=1
    text=""
    if [ "$unity" = 1 ] && [ "$inqueue" = 1 ]; then
        text="$(q heartbeat -Idle -Project "$project" -Session "$sid" | sed -n 's/^HINWEIS: //p')"
        [ "$due" = 1 ] || text=""
    elif [ "$evicted" = 1 ] || { [ "$due" = 1 ] && grep -q '"releaseDeadline":[[:space:]]*"' "$f"; }; then
        text="$(q notice -Project "$project" -Session "$sid")"
    fi
    if [ -n "$text" ]; then
        touch "$stamp"
        notes="${notes:+$notes }$text"
    fi
done

[ -n "$notes" ] || exit 0
# Nur dieses JSON auf stdout (Zusatzinfo fuer Claude, kein Fehler, nichts blockiert).
esc="$(printf '%s' "$notes" | tr '\n' ' ' | sed 's/\\/\\\\/g; s/"/\\"/g')"
printf '{"hookSpecificOutput":{"hookEventName":"%s","additionalContext":"%s"}}\n' "$event" "$esc"
exit 0
