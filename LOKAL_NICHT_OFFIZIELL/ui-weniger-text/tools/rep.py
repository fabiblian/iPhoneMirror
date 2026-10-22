"""Apply exact, unique text replacements to a file.

Usage: python -I rep.py <file> <spec.txt>

spec.txt holds blocks separated by lines of '=====' ; each block is
'<old text>\n-----\n<new text>'. Every old text must occur exactly once.
"""
import re
import sys
from pathlib import Path

target = Path(sys.argv[1])
spec = Path(sys.argv[2]).read_text(encoding="utf-8")
text = target.read_text(encoding="utf-8")
blocks = [b for b in spec.split("\n=====\n") if b.strip()]
for block in blocks:
    old, new = re.split(r"\n-----(?:\n|$)", block, maxsplit=1)
    old = old.strip("\n")
    new = new.strip("\n")
    count = text.count(old)
    if count != 1:
        sys.exit(f"{target.name}: expected 1 match, got {count}:\n{old[:300]}")
    if new == "" and old + "\n" in text:
        old += "\n"  # deleting whole lines: take their line break too
    text = text.replace(old, new)
target.write_text(text, encoding="utf-8", newline="\n")
print(f"{target.name}: {len(blocks)} replacements")
