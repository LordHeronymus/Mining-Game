#!/usr/bin/env python3
"""Turns the benchmark player's report (PerformanceBenchmarkDriver) into Artifacts/ci-results.{json,md}."""
import json
import sys
from pathlib import Path

ART = Path("Artifacts")
report = ART / "PerformanceBenchmark.txt"
seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 0.0
exit_code = sys.argv[2] if len(sys.argv) > 2 else "?"

if report.exists() and report.stat().st_size > 0:
    text = report.read_text(encoding="utf-8", errors="replace")
    status = "FAIL" if text.lstrip().startswith("FAIL") else "PASS"
else:
    tail = (ART / "player.log").read_text(encoding="utf-8", errors="replace").splitlines()[-40:] if (ART / "player.log").exists() else []
    text = f"Kein Bericht (Player-Exit {exit_code}).\n" + "\n".join(tail)
    status = "FAIL"

row = {"name": "PlayerBenchmark", "mode": "player", "status": status, "seconds": seconds, "output": text[:4000], "logErrors": []}
(ART / "ci-results.json").write_text(json.dumps({"checks": [row]}, ensure_ascii=False, indent=2), encoding="utf-8")
icon = "✅" if status == "PASS" else "❌"
(ART / "ci-results.md").write_text(
    f"### Benchmark-Player: {status}\n\n| Status | Check | Zeit |\n| --- | --- | --- |\n| {icon} | PlayerBenchmark | {seconds:.0f}s |\n\n```\n{text[:6000]}\n```\n",
    encoding="utf-8")
print(f"Benchmark {status}")
sys.exit(0 if status == "PASS" else 2)
