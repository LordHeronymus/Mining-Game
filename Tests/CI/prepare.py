#!/usr/bin/env python3
"""Prepares a CI run of Tests/*Checks.cs scripts for Assets/Editor/CI/CiCheckRunner.cs.

Copies the selected checks into Assets/Editor/CIChecks (compiled by Unity into the editor assembly)
and writes Artifacts/plan.json.

  python3 Tests/CI/prepare.py --suite smoke
  python3 Tests/CI/prepare.py --checks "CaveGenerationChecks, play:GpsRuntimeChecks"
"""
import argparse
import json
import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "Tests"
TARGET = ROOT / "Assets" / "Editor" / "CIChecks"
PLAY_MARKERS = ("Requires fresh Play", "Requires free MainMenu", "MainMenuController.IsVisible", "Requires the running main menu")


def load_suite(name):
    path = TESTS / "CI" / "suites" / f"{name}.json"
    if not path.exists():
        sys.exit(f"Suite not found: {path.relative_to(ROOT)}")
    return json.loads(path.read_text(encoding="utf-8"))["checks"]


def parse_list(text):
    entries = []
    for item in filter(None, (part.strip() for part in re.split(r"[,\s]+", text))):
        mode, _, name = item.rpartition(":")
        entries.append({"name": name, **({"mode": mode} if mode else {})})
    return entries


def complete(entry):
    name = entry["name"]
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", name):
        sys.exit(f"Invalid check name: {name}")
    source_path = TESTS / f"{name}.cs"
    if not source_path.exists():
        sys.exit(f"Check not found: Tests/{name}.cs")
    source = source_path.read_text(encoding="utf-8-sig")
    entry.setdefault("mode", "play" if any(marker in source for marker in PLAY_MARKERS) else "edit")
    entry.setdefault("resultFile", f"Temp/{name}.txt")
    # Checks driven by EditorApplication.update only report through their result file.
    entry.setdefault("waitForResultFile", "EditorApplication.update+=" in source.replace(" ", ""))
    if entry["mode"] not in ("edit", "play"):
        sys.exit(f"Invalid mode for {name}: {entry['mode']}")
    return entry, source_path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--suite", default="smoke")
    parser.add_argument("--checks", default="", help="Comma separated, optional mode prefix: play:Name")
    parser.add_argument("--plan", default=str(ROOT / "Artifacts" / "plan.json"))
    args = parser.parse_args()

    entries = parse_list(args.checks) if args.checks.strip() else load_suite(args.suite)
    if not entries:
        sys.exit("No checks selected.")

    if TARGET.exists():
        shutil.rmtree(TARGET)
    TARGET.mkdir(parents=True)
    plan = []
    for entry in entries:
        entry, source_path = complete(dict(entry))
        shutil.copy2(source_path, TARGET / source_path.name)
        plan.append(entry)

    plan_path = Path(args.plan)
    plan_path.parent.mkdir(parents=True, exist_ok=True)
    plan_path.write_text(json.dumps({"checks": plan}, indent=2), encoding="utf-8")
    for entry in plan:
        print(f"{entry['mode']:5} {entry['name']}")


if __name__ == "__main__":
    main()
