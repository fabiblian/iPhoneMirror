"""Edit iPhoneMirror string dictionaries in all four languages at once.

Usage: python -I strings.py <Localization dir> <ops.json>

ops.json is a list of operations:
  {"op": "set", "key": "K", "en": "..."}                 # change values (any of en, cn, hk, tw)
  {"op": "add", "key": "K", "after": "Anchor", "en": "...", "cn": "...", "hk": "...", "tw": "..."}
  {"op": "delete", "key": "K"}
"""
import json
import re
import sys
from pathlib import Path
from xml.sax.saxutils import escape

LANGS = {"en": "en-US", "cn": "zh-CN", "hk": "zh-HK", "tw": "zh-TW"}


def line_pattern(key):
    # Values sit in group "value"; files use both sys: and system: prefixes.
    return re.compile(r'^(?P<open>[ \t]*<(?P<ns>sys|system):String x:Key="' + re.escape(key) +
                      r'"[^>]*>)(?P<value>.*?)</(?P=ns):String>[ \t]*\n', re.M | re.S)


def main():
    directory = Path(sys.argv[1])
    ops = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8"))
    texts = {lang: (directory / f"Strings.{name}.xaml").read_text(encoding="utf-8")
             for lang, name in LANGS.items()}
    for op in ops:
        key = op["key"]
        for lang in LANGS:
            text = texts[lang]
            pattern = line_pattern(key)
            match = pattern.search(text)
            if op["op"] == "set":
                if lang not in op:
                    continue
                if not match:
                    sys.exit(f"set: {key} missing in {lang}")
                value = op[lang] if op.get("raw") else escape(op[lang])
                text = text[:match.start("value")] + value + text[match.end("value"):]
            elif op["op"] == "merge":
                # Append the "from" key's value (non-English) or use the given
                # English text, then drop the "from" key.
                source = line_pattern(op["from"]).search(text)
                if not match or not source:
                    sys.exit(f"merge: {key} or {op['from']} missing in {lang}")
                if lang in op:
                    value = escape(op[lang])
                else:
                    value = match.group("value") + op.get("separator", "") + source.group("value")
                text = text[:match.start("value")] + value + text[match.end("value"):]
                source = line_pattern(op["from"]).search(text)
                text = text[:source.start()] + text[source.end():]
            elif op["op"] == "delete":
                if not match:
                    sys.exit(f"delete: {key} missing in {lang}")
                text = text[:match.start()] + text[match.end():]
            elif op["op"] == "add":
                if match:
                    sys.exit(f"add: {key} already exists in {lang}")
                anchor = line_pattern(op["after"]).search(text)
                if not anchor:
                    sys.exit(f"add: anchor {op['after']} missing in {lang}")
                indent = re.match(r"[ \t]*", anchor.group("open")).group(0)
                ns = anchor.group("ns")
                line = f'{indent}<{ns}:String x:Key="{key}">{escape(op[lang])}</{ns}:String>\n'
                text = text[:anchor.end()] + line + text[anchor.end():]
            else:
                sys.exit(f"unknown op {op['op']}")
            texts[lang] = text
    for lang, name in LANGS.items():
        (directory / f"Strings.{name}.xaml").write_text(texts[lang], encoding="utf-8", newline="\n")
    print(f"applied {len(ops)} operations")


if __name__ == "__main__":
    main()
