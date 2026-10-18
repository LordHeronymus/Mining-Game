#!/usr/bin/env bash
# Git-Bash-Aufruf fuer Tools/editor-queue.ps1 (gleiche Argumente). Doku: Docs/Editor-Warteschlange.md
dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$dir/editor-queue.ps1")" "$@"
