#!/usr/bin/env python3
"""Actions-minute budget for the workflow "Unity Checks" (Docs/Cloud-Tests.md → Drosselung).

Compares this month's used minutes with a budget line: DAILY_PERCENT (default 3 %) of the included
minutes per started day of the month (UTC, minutes reset on the 1st). From the result:

  green   at or below the line                      everything runs
  yellow  up to YELLOW_BAND points above the line   no automatic PR runs (only PRs labelled "cloud-check"),
                                                    manual runs without `checks` use the smallest suite
  red     further above or less than RED_REMAINING  only manual runs with input force=true
          percent left

Public repositories run free on standard runners: the level is reported, nothing is throttled.

  budget.py           decides, writes CI_RUN/CI_SUITE to $GITHUB_ENV and $RUNNER_TEMP/budget.json
  budget.py --print   only prints the decision (local check; set GITHUB_TOKEN/BILLING_TOKEN)

Sources: the official usage report with BILLING_TOKEN (secret ACTIONS_BILLING_TOKEN, fine-grained
token with the account permission "Plan: Read"; minutes of public repositories left out), otherwise an estimate from this repository's runs,
otherwise "unknown" (never blocks). Errors never fail the job, except a blocked manual run.
"""
import argparse
import json
import math
import os
import sys
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

API = os.environ.get("GITHUB_API_URL", "https://api.github.com")
REPO = os.environ.get("GITHUB_REPOSITORY", "LordHeronymus/Mining-Game")
TEMP = Path(os.environ.get("RUNNER_TEMP", "/tmp"))
OUT = TEMP / "budget.json"

DAILY_PERCENT = 3.0
YELLOW_BAND = 10.0
RED_REMAINING = 5.0
DEFAULT_INCLUDED = 2000
SMALLEST_SUITE = "smoke"
RUN_LABEL = "cloud-check"

LEVEL_TEXT = {"green": "grün", "yellow": "gelb", "red": "rot", "unknown": "unbekannt"}
LEVEL_ICON = {"green": "🟢", "yellow": "🟡", "red": "🔴", "unknown": "⚪"}


def number(text, default):
    try:
        value = float(text)
        return value if value > 0 else default
    except (TypeError, ValueError):
        return default


def api(path, token):
    request = urllib.request.Request(API + path, headers={
        "Authorization": f"Bearer {token}",
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "unity-checks-budget",
    })
    with urllib.request.urlopen(request, timeout=20) as response:
        return json.loads(response.read() or b"null")


def month_bounds(now):
    start = datetime(now.year, now.month, 1, tzinfo=timezone.utc)
    end = datetime(now.year + (now.month == 12), now.month % 12 + 1, 1, tzinfo=timezone.utc)
    return start, end


def budget_line(now, daily_percent=DAILY_PERCENT):
    """Allowed percent at `now`: the daily share of every started day, at most 100."""
    start, end = month_bounds(now)
    day = min((now - start).days + 1, (end - start).days)
    return min(100.0, daily_percent * day)


def level_for(used_percent, line):
    if used_percent is None:
        return "unknown"
    if 100 - used_percent < RED_REMAINING or used_percent > line + YELLOW_BAND:
        return "red"
    return "yellow" if used_percent > line else "green"


# ---------- Sources ----------

def is_actions_minutes(item):
    product = str(item.get("product") or "").lower()
    unit = str(item.get("unitType") or "").lower()
    sku = str(item.get("sku") or "").lower()
    return product == "actions" and (unit in ("minutes", "minute") or (not unit and any(os_ in sku for os_ in ("linux", "windows", "mac"))))


def is_public(name, token, cache={}):
    """Public repositories run free on standard runners; unknown ones count."""
    if name not in cache:
        try:
            cache[name] = api(f"/repos/{name}", token).get("private") is False
        except Exception:
            cache[name] = False
    return cache[name]


def official_usage(token, now, repo_token):
    login = api("/user", token)["login"]
    body = api(f"/users/{urllib.parse.quote(login)}/settings/billing/usage?year={now.year}&month={now.month}", token)
    start, end = month_bounds(now)
    used = 0.0
    for item in (body or {}).get("usageItems") or []:
        if not isinstance(item, dict) or not is_actions_minutes(item):
            continue
        try:
            at = datetime.fromisoformat(str(item.get("date", "")).replace("Z", "+00:00"))
            if at.tzinfo is None:
                at = at.replace(tzinfo=timezone.utc)
            if not start <= at < end:
                continue
        except ValueError:
            pass
        repo = str(item.get("repositoryName") or "")
        if repo.count("/") == 1 and is_public(repo, repo_token or token):
            continue
        quantity = item.get("quantity")
        used += quantity if isinstance(quantity, (int, float)) else 0
    return round(used, 1)


def parse_time(text):
    try:
        return datetime.fromisoformat(str(text).replace("Z", "+00:00"))
    except ValueError:
        return None


def estimated_usage(token, now):
    """This repository only: each run's wall time rounded up to whole minutes (one Linux job per run)."""
    start, _ = month_bounds(now)
    since = start.strftime("%Y-%m-%d")
    used = 0
    for page in range(1, 11):
        body = api(f"/repos/{REPO}/actions/runs?created=%3E%3D{since}&per_page=100&page={page}", token)
        runs = body.get("workflow_runs") or []
        for run in runs:
            if run.get("id") == int(os.environ.get("GITHUB_RUN_ID", "0") or 0):
                continue
            begin = parse_time(run.get("run_started_at") or run.get("created_at"))
            end = parse_time(run.get("updated_at")) if run.get("status") == "completed" else now
            if begin and end and end > begin:
                used += math.ceil((end - begin).total_seconds() / 60)
        if len(runs) < 100:
            break
    return float(used)


def measure(now, included):
    billing = os.environ.get("BILLING_TOKEN", "").strip()
    token = os.environ.get("GITHUB_TOKEN", "").strip()
    notes = []
    if billing:
        try:
            return official_usage(billing, now, token), "official", notes
        except Exception as error:  # wrong permission or expired token: fall back
            notes.append(f"Offizielle Abrechnung nicht lesbar ({error}); Schätzung aus diesem Repository.")
    else:
        notes.append("Kein Secret ACTIONS_BILLING_TOKEN: Schätzung nur aus diesem Repository.")
    if token:
        try:
            if is_public(REPO, token):
                notes.append("Dieses Repository ist öffentlich: Seine Läufe zählen nicht zum Kontingent.")
                return 0.0, "estimate", notes
            return estimated_usage(token, now), "estimate", notes
        except Exception as error:
            notes.append(f"Schätzung fehlgeschlagen ({error}).")
    return None, "none", notes


# ---------- Decision ----------

def decide(level, event, action, label, force, is_public, checks, suite):
    """Returns (run, suite, reason, block): block = manual run refused (job fails)."""
    if event == "pull_request" and action == "labeled" and label != RUN_LABEL:
        return False, suite, f"Label „{label}“ gesetzt: kein Lauf nötig.", False
    if is_public:
        return True, suite, "Öffentliches Repository: Läufe sind kostenlos, keine Drosselung.", False
    if force and event == "workflow_dispatch":
        return True, suite, "Von Hand freigegeben (force).", False
    if level == "red":
        if event == "workflow_dispatch":
            return False, suite, "Drosselung rot: nur von Hand freigegebene Läufe (Eingabe force).", True
        return False, suite, "Drosselung rot: automatische Läufe pausiert.", False
    if level == "yellow":
        if event == "pull_request" and label != RUN_LABEL:
            return False, suite, f"Drosselung gelb: PR-Lauf nur mit Label „{RUN_LABEL}“ (einmal am Schluss).", False
        if event == "workflow_dispatch" and not checks and suite != SMALLEST_SUITE:
            return True, SMALLEST_SUITE, f"Drosselung gelb: Suite „{suite}“ durch „{SMALLEST_SUITE}“ ersetzt.", False
    return True, suite, "", False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--print", action="store_true", help="only print the decision")
    args = parser.parse_args()

    now = datetime.now(timezone.utc)
    included = number(os.environ.get("INCLUDED_MINUTES"), DEFAULT_INCLUDED)
    daily = number(os.environ.get("DAILY_PERCENT"), DAILY_PERCENT)
    used, source, notes = measure(now, included)
    line = budget_line(now, daily)
    percent = None if used is None else round(used / included * 100, 1)
    level = level_for(percent, line)

    event = os.environ.get("EVENT", "")
    event_data = {}
    try:
        event_data = json.loads(Path(os.environ.get("GITHUB_EVENT_PATH", "")).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        pass
    action = event_data.get("action", "")
    pr_labels = [l.get("name") for l in (event_data.get("pull_request") or {}).get("labels") or []]
    label = (event_data.get("label") or {}).get("name") or (RUN_LABEL if RUN_LABEL in pr_labels else "")
    is_public = (event_data.get("repository") or {}).get("private") is False
    force = os.environ.get("FORCE", "").lower() == "true"
    suite = os.environ.get("SUITE", SMALLEST_SUITE)
    run, suite, reason, block = decide(level, event, action, label, force, is_public, os.environ.get("CHECKS", ""), suite)

    result = {
        "level": level, "level_text": LEVEL_TEXT[level], "used": used, "included": included,
        "percent": percent, "line": round(line, 1), "daily_percent": daily, "source": source,
        "public": is_public, "run": run, "reason": reason, "notes": notes, "checked": now.isoformat(timespec="seconds"),
    }
    summary = (f"{LEVEL_ICON[level]} Drosselung {LEVEL_TEXT[level]}: "
               + (f"{used:g} / {included:g} min ({percent:g} %), Budget-Linie {line:g} %" if used is not None else "Verbrauch unbekannt")
               + f" · Quelle: {dict(official='genau', estimate='geschätzt (nur dieses Repo)', none='keine')[source]}")
    print(summary)
    for note in notes:
        print(note)
    if reason:
        print(reason)
    if args.print:
        return

    try:
        OUT.write_text(json.dumps(result, ensure_ascii=False), encoding="utf-8")
        with open(os.environ["GITHUB_ENV"], "a", encoding="utf-8") as env:
            env.write(f"CI_RUN={'1' if run else '0'}\nCI_SUITE={suite}\n")
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as out:
            out.write(f"### {summary}\n\n" + (f"**{reason}**\n\n" if reason else "") + "".join(f"- {n}\n" for n in notes) + "\n")
    except (KeyError, OSError) as error:
        print(f"Could not write the decision: {error}", file=sys.stderr)
    if not run:
        print(f"::{'error' if block else 'notice'} title=Unity Checks übersprungen::{reason}")
    elif level in ("yellow", "red") and not is_public:
        print(f"::warning title=Drosselung {LEVEL_TEXT[level]}::{reason or summary}")
    if block:
        sys.exit(1)


def fail_open(error):
    # An unexpected error must not stop the tests: run as without throttling.
    print(f"Budget check failed, running unthrottled: {error}", file=sys.stderr)
    try:
        with open(os.environ["GITHUB_ENV"], "a", encoding="utf-8") as env:
            env.write("CI_RUN=1\n")
    except (KeyError, OSError):
        pass


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception as error:
        fail_open(error)
