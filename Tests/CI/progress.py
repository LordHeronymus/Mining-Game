#!/usr/bin/env python3
"""Live status of a Unity Checks run for the cloud dashboard (Tools/CloudDashboard).

Each run gets one comment in the issue labelled "cloud-status". The comment shows a short
summary and carries the full status as base64 JSON in a hidden HTML comment.

  progress.py start                     creates the comment
  progress.py watch                     updates it every 15 s from Artifacts/*.log (background)
  progress.py final --conclusion X      writes the results and stops the watcher
  progress.py skipped                   one final comment for a run the budget skipped (Tests/CI/budget.py)

Errors are printed but never fail the job: the live status must not break the tests.
"""
import argparse
import base64
import json
import os
import re
import sys
import time
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

API = os.environ.get("GITHUB_API_URL", "https://api.github.com")
REPO = os.environ.get("GITHUB_REPOSITORY", "")
TOKEN = os.environ.get("GITHUB_TOKEN", "")
RUN_ID = os.environ.get("GITHUB_RUN_ID", "0")
RUN_NUMBER = os.environ.get("GITHUB_RUN_NUMBER", "?")
REF = os.environ.get("GITHUB_HEAD_REF") or os.environ.get("GITHUB_REF_NAME", "")
SUITE = os.environ.get("CI_SUITE") or os.environ.get("SUITE", "")
CHECKS = os.environ.get("CHECKS", "")
LABEL = "cloud-status"
TEMP = Path(os.environ.get("RUNNER_TEMP", "/tmp"))
STATE = TEMP / "cloud-status-comment.json"
BUDGET = TEMP / "budget.json"  # written by Tests/CI/budget.py
STOP = TEMP / "cloud-status-stop"
ART = Path("Artifacts")
INTERVAL = 15

PHASES = {
    "prepare": "Vorbereitung",
    "license": "Lizenz wird aktiviert",
    "unity": "Unity startet",
    "import": "Projekt wird importiert",
    "compile": "Skripte werden kompiliert",
    "checks": "Checks laufen",
    "done": "Fertig",
}

# Known failure causes, shown in the dashboard instead of a bare "failure".
PROBLEMS = [
    ("No seat available", "Unity hat dem Konto keinen Personal-Platz zugewiesen (id.unity.com prüfen)."),
    ("Personal licence activation failed", "Aktivierung der Personal-Lizenz über das Unity-Konto fehlgeschlagen (UNITY_EMAIL/UNITY_PASSWORD prüfen)."),
    ("No valid Unity Editor license found", "Unity-Lizenz ungültig oder nicht aktiviert (Secrets UNITY_LICENSE/UNITY_EMAIL/UNITY_PASSWORD prüfen)."),
    ("Scripts have compiler errors", "Compilerfehler im Projekt oder in einem Check."),
    ("No space left on device", "Speicherplatz auf dem Runner voll."),
]

CHECK_START = re.compile(r"^\[CI\] \((\d+)/(\d+)\) (\S+) \[(\w+)\]")
CHECK_END = re.compile(r"^\[CI\] (PASS|FAIL) (\S+) \(([\d.]+)s\)")


def api(method, path, body=None):
    request = urllib.request.Request(
        API + path,
        method=method,
        data=json.dumps(body).encode() if body is not None else None,
        headers={
            "Authorization": f"Bearer {TOKEN}",
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "unity-checks-progress",
        },
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        data = response.read()
        return json.loads(data) if data else None


def read(path):
    try:
        return Path(path).read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""


def now():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def find_issue():
    issues = api("GET", f"/repos/{REPO}/issues?labels={LABEL}&state=all&per_page=1")
    if issues:
        return issues[0]["number"]
    body = ("Live-Status der Cloud-Tests (Workflow „Unity Checks“). Jeder Lauf schreibt hier einen Kommentar, "
            "den das Dashboard unter `Tools/CloudDashboard` liest. Siehe `Docs/Cloud-Tests.md`.")
    return api("POST", f"/repos/{REPO}/issues", {"title": "Cloud-Tests Live-Status", "body": body, "labels": [LABEL]})["number"]


def total_assets():
    try:
        return sum(1 for _ in Path("Assets").rglob("*.meta"))
    except OSError:
        return 0


def budget():
    try:
        return json.loads(read(BUDGET) or "null")
    except ValueError:
        return None


def build(final=None, conclusion=None):
    plan = []
    try:
        plan = json.loads(read(ART / "plan.json") or "{}").get("checks", [])
    except ValueError:
        pass
    checks = [{"name": c["name"], "mode": c.get("mode", "edit"), "status": "pending"} for c in plan]
    by_name = {c["name"]: c for c in checks}

    log = read(ART / "editor.log")
    player_log = read(ART / "player.log")
    if player_log:
        # Benchmark player running (suite performance): the build is done, show the player's log.
        for c in checks:
            if c["mode"] == "player" and c["status"] == "pending":
                c["status"] = "running"
        log = player_log
    lines = log.splitlines()
    imported = 0
    compiling = False
    for i, line in enumerate(lines):
        if line.startswith("Start importing "):
            imported += 1
        elif "Starting: " in line and "csc" in line or line.startswith("Compiling assembly") or "- Starting compile" in line:
            compiling = True
        start = CHECK_START.match(line)
        if start and start.group(3) in by_name:
            by_name[start.group(3)]["status"] = "running"
            continue
        end = CHECK_END.match(line)
        if end and end.group(2) in by_name:
            entry = by_name[end.group(2)]
            entry["status"], entry["seconds"] = end.group(1), float(end.group(3))
            following = next((l.strip() for l in lines[i + 1:i + 4] if l.strip()), "")
            entry["message"] = following[:300]

    results = None
    if final:
        try:
            results = json.loads(read(ART / "ci-results.json") or "{}").get("checks")
        except ValueError:
            results = None
        for row in results or []:
            entry = by_name.get(row.get("name"))
            if entry:
                entry.update(status=row.get("status", entry["status"]), seconds=row.get("seconds"),
                             message=(row.get("output") or "").strip().split("\n")[0][:300])
            row["output"] = (row.get("output") or "")[:2500]

    started = any(c["status"] != "pending" for c in checks)
    if final:
        phase = "done"
    elif started:
        phase = "checks"
    elif log and any(c["mode"] == "player" for c in checks):
        phase = "compile"  # building the benchmark player (shaders), shown as "Kompilieren"
    elif log:
        tail = "\n".join(lines[-30:])
        phase = "compile" if compiling and ("csc" in tail or "Compil" in tail) else "import" if imported else "unity"
    elif read(ART / "activation.log"):
        phase = "license"
    else:
        phase = "prepare"

    source = lines if lines else read(ART / "activation.log").splitlines()
    tail = [l[:300] for l in source[-400:] if l.strip()][-60:]
    problem = read(ART / "abort-reason.txt").strip() or next(
        (text for marker, text in PROBLEMS if marker in log + read(ART / "activation.log")), None)
    errors = sorted({l.strip()[:300] for l in lines if re.search(r"error CS\d+", l)})[:30]
    return {
        "version": 1,
        "run_id": int(RUN_ID),
        "run_number": RUN_NUMBER,
        "ref": REF,
        "suite": CHECKS or SUITE,
        "phase": phase,
        "phase_text": "Spiel wird gebaut (Shader)" if phase == "compile" and any(c["mode"] == "player" for c in checks) else PHASES[phase],
        "updated": now(),
        "imported": imported,
        "assets": total_assets(),
        "checks": checks,
        "compile_errors": errors,
        "problem": problem,
        "log_tail": tail,
        "conclusion": conclusion,
        "results": results,
        "budget": budget(),
        "previews": sorted(str(path.relative_to(ART / "previews")) for path in (ART / "previews").rglob("*.png")) if final else [],
    }


ICONS = {"pending": "⏳", "running": "▶️", "PASS": "✅", "FAIL": "❌"}
LEVEL_ICONS = {"green": "🟢", "yellow": "🟡", "red": "🔴", "unknown": "⚪"}


def budget_line(data):
    if not data:
        return ""
    used = f"{data['percent']:g} % verbraucht, Budget-Linie {data['line']:g} %" if data.get("percent") is not None else "Verbrauch unbekannt"
    free = " · öffentlich, kostenlos" if data.get("public") else ""
    reason = f" · {data['reason']}" if data.get("reason") else ""
    return f"{LEVEL_ICONS.get(data.get('level'), '')} Drosselung {data.get('level_text', '?')} ({used}){free}{reason}\n\n"


def render(status):
    # GitHub comments hold at most 65536 characters.
    body = render_once(status)
    if len(body) > 60000:
        status["log_tail"] = status["log_tail"][-15:]
        for row in status.get("results") or []:
            row["output"] = row["output"][:400]
        body = render_once(status)
    return body


def render_once(status):
    head = f"<!-- cloud-status run={status['run_id']} -->\n"
    title = f"**Unity Checks #{status['run_number']}** · `{status['ref']}` · {status['phase_text']}"
    if status.get("conclusion"):
        title += f" ({status['conclusion']})"
    rows = "\n".join(f"| {ICONS.get(c['status'], '')} | {c['name']} | {c['mode']} |" for c in status["checks"])
    table = "| | Check | Modus |\n| --- | --- | --- |\n" + rows if rows else ""
    data = base64.b64encode(json.dumps(status, ensure_ascii=False).encode()).decode()
    shots = f"\n\nVorschaubilder: {len(status['previews'])} (Branch `ci-previews`, `runs/{status['run_number']}/`, abholen mit `Tools/ci-previews.sh {status['run_number']}`)" if status.get("previews") else ""
    return f"{head}{title}\n\n{budget_line(status.get('budget'))}Aktualisiert {status['updated']}\n\n{table}{shots}\n\n<!-- cloud-status-data {data} -->"


def save(status):
    state = json.loads(read(STATE) or "{}")
    if not state.get("comment"):
        return
    api("PATCH", f"/repos/{REPO}/issues/comments/{state['comment']}", {"body": render(status)})


def start():
    issue = find_issue()
    comment = api("POST", f"/repos/{REPO}/issues/{issue}/comments", {"body": render(build())})
    STATE.write_text(json.dumps({"issue": issue, "comment": comment["id"]}))
    print(f"Live status: issue #{issue}, comment {comment['id']}")


def watch():
    while not STOP.exists():
        try:
            save(build())
        except Exception as error:  # keep watching, the next round may work
            print(f"Update failed: {error}", file=sys.stderr)
        for _ in range(INTERVAL):
            if STOP.exists():
                return
            time.sleep(1)


def final(conclusion):
    STOP.write_text("stop")
    save(build(final=True, conclusion=conclusion))


def skipped():
    status = build(final=True, conclusion="skipped")
    status["phase_text"] = "Übersprungen (Drosselung)"
    status["problem"] = (status.get("budget") or {}).get("reason") or None
    issue = find_issue()
    api("POST", f"/repos/{REPO}/issues/{issue}/comments", {"body": render(status)})
    print(f"Live status: skipped run noted in issue #{issue}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=["start", "watch", "final", "skipped"])
    parser.add_argument("--conclusion", default="")
    args = parser.parse_args()
    if not (REPO and TOKEN):
        print("GITHUB_REPOSITORY/GITHUB_TOKEN missing, live status disabled.")
        return
    try:
        {"start": start, "watch": watch, "final": lambda: final(args.conclusion), "skipped": skipped}[args.command]()
    except Exception as error:
        print(f"Live status {args.command} failed: {error}", file=sys.stderr)


if __name__ == "__main__":
    main()
