#!/usr/bin/env python3
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
# Minimal stand-in for `reuse lint` (the reuse tool is not always installed): every tracked file outside
# upstream/ and LICENSES/ must have an SPDX header or a REUSE.toml annotation, every identifier used must
# have a text in LICENSES/, and every text in LICENSES/ must be used. Read-only; exit 1 on any problem.
import fnmatch, os, re, subprocess, sys, tomllib

repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
os.chdir(repo)
files = [f for f in subprocess.run(["git", "ls-files", "-z"], capture_output=True, check=True).stdout.decode().split("\0") if f]
submodules = set(subprocess.run(["git", "config", "--file", ".gitmodules", "--get-regexp", "path"],
                                capture_output=True, text=True).stdout.split()[1::2])
ignored = re.compile(r"^(LICENSES/|REUSE\.toml$|LICENSE$|\.gitmodules$)")
with open("REUSE.toml", "rb") as handle:
    annotations = tomllib.load(handle).get("annotations", [])

def glob_match(pattern, path):
    # REUSE.toml: "*" does not cross "/", "**" does.
    rx = "".join(".*" if part == "**" else "[^/]*" if part == "*" else re.escape(part)
                 for part in re.split(r"(\*\*|\*)", pattern))
    return re.fullmatch(rx, path) is not None

def toml_info(path):
    found = None
    for annotation in annotations:
        paths = annotation["path"] if isinstance(annotation["path"], list) else [annotation["path"]]
        if any(glob_match(p, path) for p in paths): found = annotation  # last match wins
    return found

# REUSE-IgnoreStart
tag = re.compile(r"SPDX-License-Identifier:\s*([^\n*>]+?)\s*(?:-->|\*/)?\s*$", re.M)
copyright_tag = re.compile(r"SPDX-FileCopyrightText:", re.M)
# REUSE-IgnoreEnd
ignore_block = re.compile(r"REUSE-IgnoreStart.*?(?:REUSE-IgnoreEnd|\Z)", re.S)
problems, used = [], set()
counts = {"header": 0, "toml": 0}
for path in files:
    if ignored.match(path) or any(path == s or path.startswith(s + "/") for s in submodules): continue
    if not os.path.isfile(path): continue
    expression = None
    try:
        with open(path, encoding="utf-8") as handle: head = ignore_block.sub("", handle.read())[:4096]
        match = tag.search(head)
        if match and copyright_tag.search(head): expression = match.group(1); counts["header"] += 1
    except UnicodeDecodeError:
        pass
    if expression is None:
        annotation = toml_info(path)
        if annotation is None: problems.append("sem licença: " + path); continue
        expression = annotation["SPDX-License-Identifier"]; counts["toml"] += 1
    for identifier in re.split(r"\s+(?:AND|OR|WITH)\s+|[()]", expression):
        identifier = identifier.strip()
        if identifier: used.add(identifier)

available = {os.path.splitext(name)[0] for name in os.listdir("LICENSES")}
for identifier in sorted(used - available): problems.append("texto ausente em LICENSES/: " + identifier)
for identifier in sorted(available - used): problems.append("licença não usada em LICENSES/: " + identifier)
for problem in problems: print("FAIL", problem)
print(f"{counts['header']} arquivos com cabeçalho SPDX, {counts['toml']} cobertos por REUSE.toml; "
      f"licenças usadas: {', '.join(sorted(used))}")
sys.exit(1 if problems else 0)
