#!/usr/bin/env bash
# Git-Bash-Aufruf fuer Tools/worktree-sparse.ps1 (gleiche Argumente). Doku: Docs/Arbeitsordner-ohne-Design.md
dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$dir/worktree-sparse.ps1")" "$@"
