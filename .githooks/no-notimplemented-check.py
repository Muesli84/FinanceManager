#!/usr/bin/env python3
"""NotImplementedException / throw-only stub check for C# files.

Forbids:
  1. NotImplementedException anywhere in the code.
  2. Methods, constructors, property getters/setters whose entire body
     consists of a single throw statement (placeholder stubs), regardless
     of the exception type thrown.

Background: such stubs tend to go unnoticed and turn into runtime errors
instead of compile-time errors. Members should either be fully implemented
or not exist yet.

By default (as used in pre-commit) this only WARNS and always exits 0, so
work in progress is allowed while a feature is being built. Pass --strict
(as used in pre-push) to turn findings into a hard failure (exit 1), so
stubs cannot leave the machine.

Run as a pre-commit hook (default: only staged files) or with --all to scan
the entire repository.
"""
import argparse
import re
import subprocess
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
sys.stderr.reconfigure(encoding='utf-8', errors='replace')

EXCLUDED_DIRS = {'.git', 'bin', 'obj', 'TestResults', 'node_modules', '.vs', 'packages'}

MODIFIER = (
    r"(?:public|private|protected|internal|static|virtual|override|"
    r"abstract|async|sealed|extern|new|readonly)"
)

# Methods/constructors with a block body consisting of just one throw
# ("=" stays allowed so default parameters, e.g. "int x = 5", don't exclude
# the signature from the check; ";", "{", "}" still mark statement
# boundaries and prevent matching into unrelated code).
STUB_BLOCK_RE = re.compile(
    rf"{MODIFIER}[^{{}};]*\)\s*\{{\s*throw\s+new\s+\w+\s*\([^;]*\)\s*;\s*\}}"
)

# Expression-bodied methods/properties that only throw
STUB_EXPR_RE = re.compile(
    rf"{MODIFIER}[^{{}};]*=>\s*throw\s+new\s+\w+\s*\([^;]*\)\s*;"
)

# get/set/init accessors whose body consists of just one throw
ACCESSOR_EXPR_RE = re.compile(r"\b(?:get|set|init)\s*=>\s*throw\s+new\s+\w+\s*\([^;]*\)\s*;")
ACCESSOR_BLOCK_RE = re.compile(r"\b(?:get|set|init)\s*\{\s*throw\s+new\s+\w+\s*\([^;]*\)\s*;\s*\}")


def run(*args):
    return subprocess.run(args, capture_output=True, text=True)


def repo_root():
    res = run('git', 'rev-parse', '--show-toplevel')
    if res.returncode != 0:
        print('ERROR: not inside a git repository', file=sys.stderr)
        return None
    return Path(res.stdout.strip())


def staged_files():
    res = run('git', 'diff', '--cached', '--name-only', '--diff-filter=ACM')
    if res.returncode != 0:
        return []
    return [p for p in res.stdout.splitlines() if p]


def all_cs_files(root):
    files = []
    for p in root.rglob('*.cs'):
        if any(part in EXCLUDED_DIRS for part in p.parts):
            continue
        files.append(str(p.relative_to(root).as_posix()))
    return files


def strip_comments_and_strings(text):
    """Replaces comments and string/char literals with spaces, keeping
    newlines so line numbers stay correct."""
    out = []
    i = 0
    n = len(text)
    while i < n:
        two = text[i:i + 2]
        if two == "//":
            j = text.find("\n", i)
            if j == -1:
                j = n
            out.append(" " * (j - i))
            i = j
        elif two == "/*":
            j = text.find("*/", i + 2)
            j = n if j == -1 else j + 2
            out.append(re.sub(r"[^\n]", " ", text[i:j]))
            i = j
        elif text[i] in ('"', "'"):
            quote = text[i]
            j = i + 1
            while j < n and text[j] != quote:
                j += 2 if text[j] == "\\" and j + 1 < n else 1
            j = min(j + 1, n)
            out.append(re.sub(r"[^\n]", " ", text[i:j]))
            i = j
        else:
            out.append(text[i])
            i += 1
    return "".join(out)


def find_stub_issues(content):
    """Returns a list of issue description strings for one file's content."""
    clean = strip_comments_and_strings(content)
    source_lines = content.splitlines()

    def line_of(pos):
        return clean.count("\n", 0, pos) + 1

    def source_line_text(ln):
        return source_lines[ln - 1].strip() if 0 < ln <= len(source_lines) else ""

    found = {}  # line -> message, dedupe

    for m in re.finditer(r"\bNotImplementedException\b", clean):
        ln = line_of(m.start())
        found[ln] = f"  Zeile {ln}: NotImplementedException verwendet — {source_line_text(ln)[:120]}"

    for m in STUB_BLOCK_RE.finditer(clean):
        ln = line_of(m.start())
        found.setdefault(ln, f"  Zeile {ln}: Methode besteht nur aus einem throw-Statement (Stub)")

    for m in STUB_EXPR_RE.finditer(clean):
        ln = line_of(m.start())
        found.setdefault(ln, f"  Zeile {ln}: Expression-bodied Member besteht nur aus einem throw-Statement (Stub)")

    for pattern in (ACCESSOR_EXPR_RE, ACCESSOR_BLOCK_RE):
        for m in pattern.finditer(clean):
            ln = line_of(m.start())
            found.setdefault(ln, f"  Zeile {ln}: Accessor besteht nur aus einem throw-Statement (Stub)")

    return [found[ln] for ln in sorted(found)]


def parse_args():
    parser = argparse.ArgumentParser(description='NotImplementedException / throw-only stub check')
    parser.add_argument('--all', action='store_true', help='scan all .cs files, not only staged ones')
    parser.add_argument('--strict', action='store_true', help='exit 1 on findings instead of only warning')
    return parser.parse_args()


def main():
    args = parse_args()
    root = repo_root()
    if root is None:
        return 1

    if args.all:
        files = all_cs_files(root)
        scan_mode = 'all'
    else:
        files = [f for f in staged_files() if f.endswith('.cs')]
        scan_mode = 'staged'

    checked = 0
    any_found = False
    for rel in files:
        path = root / rel
        if not path.exists():
            continue
        checked += 1
        try:
            content = path.read_text(encoding='utf-8', errors='replace')
        except OSError:
            continue

        issues = find_stub_issues(content)
        if issues:
            any_found = True
            level = 'ERROR' if args.strict else 'WARNING'
            print(f'{level}: verbotene Platzhalter-Implementierung in {rel}:')
            print('\n'.join(issues))
            print('  -> NotImplementedException ist nicht erlaubt, und Methoden/Properties/Accessoren')
            print('     dürfen nicht nur aus einem throw-Statement bestehen. Vollständig implementieren')
            print('     oder das Member noch nicht anlegen.')
            print()

    if any_found:
        if args.strict:
            return 1
        print('(Nur Warnung beim Commit — muss vor dem Push vollständig implementiert sein.)')
        return 0

    print(f'OK: {checked} {scan_mode} .cs file(s) checked, no stub implementations found.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
