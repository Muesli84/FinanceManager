#!/usr/bin/env python3
"""Check for hardcoded UI strings in Razor files that should be localized.

Run as a pre-commit hook (default: only staged files) or with --all to scan
the entire repository.

Flags string literals in localizable HTML attributes (title, placeholder,
alt, aria-label, label, tooltip) and multi-word text nodes that look like
natural-language UI text rather than code/identifiers, so they can be
replaced with @L["Key"] calls (or a developer can confirm no localization
is needed).
"""
import argparse
import re
import subprocess
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
sys.stderr.reconfigure(encoding='utf-8', errors='replace')

EXCLUDED_DIRS = {'.git', 'bin', 'obj', 'TestResults', 'node_modules', '.vs', 'packages'}

LOCALIZABLE_ATTRS = {'title', 'placeholder', 'alt', 'aria-label', 'label', 'tooltip'}


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


def all_razor_files(root):
    files = []
    for p in root.rglob('*.razor'):
        if any(part in EXCLUDED_DIRS for part in p.parts):
            continue
        files.append(str(p.relative_to(root).as_posix()))
    return files


def is_code_like(text):
    text = text.strip()
    if not text or text.isdigit():
        return True
    if text.startswith(('http', '/', '#', '.', '../', 'sz-', 'bi-')):
        return True
    # Looks like a CSS class list, identifier, or enum value (no spaces, no German umlauts)
    if re.match(r'^[a-z][a-zA-Z0-9_\-]*$', text):
        return True
    # Only symbols/punctuation
    if not re.search(r'[a-zA-ZäöüÄÖÜß]', text):
        return True
    return False


def check_file(content):
    findings = []
    lines = content.splitlines()
    in_code_block = False
    code_depth = 0

    for lineno, line in enumerate(lines, 1):
        stripped = line.strip()

        # Track @code { ... } blocks — skip them entirely
        if re.match(r'@code\s*\{', stripped):
            in_code_block = True
            code_depth = 1
            continue
        if in_code_block:
            code_depth += stripped.count('{') - stripped.count('}')
            if code_depth <= 0:
                in_code_block = False
            continue

        # Skip Razor directives and comment lines
        if re.match(r'@(page|using|inject|inherits|namespace|typeparam|model|layout|addTagHelper)\b', stripped):
            continue
        if stripped.startswith(('//', '<!--', '*', '@*')):
            continue

        # --- Check localizable attributes ---
        # Matches: attr="value"  where value has no @ or { (= not already a Razor expression)
        for m in re.finditer(r'\b([\w-]+)="([^"@{][^"]*)"', line, re.IGNORECASE):
            attr_name = m.group(1).lower()
            attr_val = m.group(2)
            if attr_name not in LOCALIZABLE_ATTRS:
                continue
            if is_code_like(attr_val):
                continue
            if not re.search(r'[a-zA-ZäöüÄÖÜß]', attr_val):
                continue
            findings.append(f'  Zeile {lineno}: {attr_name}="{attr_val}"')

        # --- Check text nodes: >some text</ ---
        # Require spaces (multi-word) to reduce false positives on single technical tokens
        for m in re.finditer(r'>([^<>@{}]+)</', line):
            text = m.group(1).strip()
            if not text or ' ' not in text:
                continue
            if is_code_like(text):
                continue
            if not re.search(r'[a-zA-ZäöüÄÖÜß]', text):
                continue
            findings.append(f'  Zeile {lineno}: Textknoten "{text}"')

    return findings


def parse_args():
    parser = argparse.ArgumentParser(description='Razor localization check')
    parser.add_argument('--all', action='store_true', help='scan all .razor files, not only staged ones')
    return parser.parse_args()


def main():
    args = parse_args()
    root = repo_root()
    if root is None:
        return 1

    if args.all:
        files = all_razor_files(root)
        scan_mode = 'all'
    else:
        files = [f for f in staged_files() if f.endswith('.razor')]
        scan_mode = 'staged'

    checked = 0
    failed = False
    for rel in files:
        path = root / rel
        if not path.exists():
            continue
        checked += 1
        try:
            content = path.read_text(encoding='utf-8', errors='replace')
        except OSError:
            continue

        findings = check_file(content)
        if findings:
            failed = True
            print(f'ERROR: möglicherweise hartcodierte UI-Strings in {rel}:')
            print('\n'.join(findings))
            print('  → Durch @L["SchlüsselName"]-Aufrufe ersetzen oder bestätigen, dass kein Lokalisierungsbedarf besteht.')
            print()

    if failed:
        return 1

    print(f'OK: {checked} {scan_mode} .razor file(s) checked, no hardcoded UI strings found.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
