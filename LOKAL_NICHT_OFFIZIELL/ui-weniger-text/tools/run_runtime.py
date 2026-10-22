"""Run selected runtime test switches one after another and report exit codes.

Usage: python -I run_runtime.py <runtime test dll> <output dir> [switch ...]
"""
import subprocess
import sys
from pathlib import Path

DEFAULT = [
    ["--info-tip", "OUT"], ["--language-display-audit", "OUT"], ["--theme-content", "OUT"],
    ["--preview-shell-ui-audit", "OUT"], ["--ui-regression"], ["--interaction-regression"],
    ["--localization-audit"], ["--taiwan-localization"], ["--adaptive-toolbar", "OUT"],
    ["--button-borders", "OUT"], ["--tray-mode", "OUT"], ["--component-download-ui", "OUT"],
    ["--control-states", "OUT"], ["--reverse-control-countdown"], ["--workspace-regression"],
    ["--keyboard-focus"], ["--shortcut-search", "OUT"], ["--audio-persistence", "OUT"],
    ["--volume-controls", "OUT"], ["--control-binding", "OUT"],
]

dll, out = sys.argv[1], Path(sys.argv[2])
wanted = set(sys.argv[3:])
out.mkdir(parents=True, exist_ok=True)
for spec in DEFAULT:
    name = spec[0]
    if wanted and name not in wanted:
        continue
    target = out / name.strip("-")
    args = [str(target) if part == "OUT" else part for part in spec]
    try:
        result = subprocess.run(["dotnet", dll, *args], capture_output=True, text=True,
                                encoding="utf-8", errors="replace", timeout=1800)
        code, text = result.returncode, (result.stdout + result.stderr).strip().splitlines()
    except subprocess.TimeoutExpired:
        code, text = "timeout", []
    (out / f"{name.strip('-')}.log").write_text("\n".join(text), encoding="utf-8")
    last = next((line for line in reversed(text) if line.strip()), "")
    print(f"{name}: exit={code} | {last[:220]}", flush=True)
