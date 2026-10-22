"""List App string keys that no source file names, minus keys a code prefix can build.

Usage: python -I unused_keys.py <repo root>
"""
import re
import sys
from pathlib import Path

root = Path(sys.argv[1])
strings = (root / "src/App/Localization/Strings.en-US.xaml").read_text(encoding="utf-8")
keys = re.findall(r'<(?:sys|system):String x:Key="([^"]+)"', strings)

sources = []
for folder in ["src/App", "src/SharedUI", "src/App.Runtime.Tests", "src/App.Logic.Tests", "scripts", "tests", "tools"]:
    for path in (root / folder).rglob("*"):
        if path.suffix not in {".cs", ".xaml", ".py", ".ps1", ".js", ".json"}:
            continue
        if any(part in {"bin", "obj", "native"} for part in path.parts):
            continue
        if path.name.startswith("Strings."):
            continue
        sources.append(path.read_text(encoding="utf-8", errors="ignore"))
corpus = "\n".join(sources)

# String literals used to build keys: "Prefix" + something, or $"Prefix{...}".
prefixes = set(re.findall(r'"([A-Za-z][A-Za-z0-9]*)"\s*\+', corpus))
prefixes |= set(re.findall(r'\$"([A-Za-z][A-Za-z0-9]*)\{', corpus))

unused, prefixed = [], []
for key in keys:
    if re.search(r'\b' + re.escape(key) + r'\b', corpus):
        continue
    builder = next((p for p in prefixes if key.startswith(p) and len(p) >= 4), None)
    (prefixed if builder else unused).append((key, builder))

print("UNUSED:", len(unused))
for key, _ in unused:
    print("  ", key)
print("BUILT FROM A PREFIX (kept):", len(prefixed))
for key, builder in prefixed:
    print("  ", key, "<-", builder)
