#!/usr/bin/env bash
# Git-Bash-Aufruf fuer Tools/disk-cleanup.ps1 (gleiche Argumente). Doku: Docs/Speicher-Bereinigung.md
dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$dir/disk-cleanup.ps1")" "$@"
