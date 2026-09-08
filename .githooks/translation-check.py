#!/usr/bin/env python3
"""Localization check.

Run as a pre-commit hook (default: only staged files) or with --all to scan
the entire repository for unused/missing localization keys.

1. Scans staged .cs/.razor/.cshtml files for IStringLocalizer indexer usages and
   ensures every key exists in at least one .resx file in the repository.
2. Groups .resx files by package (neutral + language variants in the same
   directory) and ensures that every key defined in one file of a package is
   present in all other files of the same package.
3. Validates the required resx headers (resmimetype, reader, writer) in every
   .resx file to catch formatting issues such as `Text/microsoft-resx`.
"""
import argparse
import re
import subprocess
import sys
from pathlib import Path
from xml.etree import ElementTree as ET

LOCALIZER_VAR = r"(?:this\.)?(?:[A-Za-z_][A-Za-z0-9_]*[lL]ocalizer|[Ll]ocalizer)"
KEY_PATTERNS = [
    re.compile(rf'{LOCALIZER_VAR}\??\s*\[\s*"(?P<key>[^"]+)"'),
    re.compile(rf"{LOCALIZER_VAR}\??\s*\[\s*'(?P<key>[^']+)'"),
    re.compile(r'GetRequiredService<IStringLocalizer<[^>]+>>\(\)\s*\[\s*"(?P<key>[^"]+)"'),
    re.compile(r"GetRequiredService<IStringLocalizer<[^>]+>>\(\)\s*\[\s*'(?P<key>[^']+)'"),
]

RESX_CULTURE_RE = re.compile(r'^(.*?)(?:\.([a-zA-Z]{2}(?:-[a-zA-Z]{2})?))?\.resx$')
EXCLUDED_DIRS = {'.git', 'bin', 'obj', 'TestResults', 'node_modules', '.vs', 'packages'}


def run(*args):
    res = subprocess.run(args, capture_output=True, text=True)
    return res


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


def all_source_files(root):
    files = []
    for ext in ('*.cs', '*.razor', '*.cshtml'):
        for p in root.rglob(ext):
            if any(part in EXCLUDED_DIRS for part in p.parts):
                continue
            files.append(str(p.relative_to(root).as_posix()))
    return files


def resx_files(root):
    files = []
    for p in root.rglob('*.resx'):
        if any(part in EXCLUDED_DIRS for part in p.parts):
            continue
        files.append(p)
    return files


def resx_keys(path):
    try:
        tree = ET.parse(path)
    except ET.ParseError as e:
        print(f'ERROR: cannot parse {path}: {e}', file=sys.stderr)
        return set()
    return {d.get('name') for d in tree.getroot().iter('data') if d.get('name')}


def resx_header_errors(path):
    """Validate required ResX headers. Returns a list of error strings."""
    errors = []
    try:
        tree = ET.parse(path)
    except ET.ParseError as e:
        return [f'cannot parse XML: {e}']

    resheaders = {}
    for h in tree.getroot().iter('resheader'):
        name = h.get('name')
        if name:
            resheaders[name] = ''.join(h.itertext()).strip()

    resmimetype = resheaders.get('resmimetype', '')
    if resmimetype != 'text/microsoft-resx':
        errors.append(f'invalid resmimetype: {resmimetype!r} (expected "text/microsoft-resx")')

    reader = resheaders.get('reader', '')
    if not reader or 'System.Resources.ResXResourceReader' not in reader:
        errors.append('missing or invalid reader resheader')

    writer = resheaders.get('writer', '')
    if not writer or 'System.Resources.ResXResourceWriter' not in writer:
        errors.append('missing or invalid writer resheader')

    return errors


def resx_base(path):
    m = RESX_CULTURE_RE.match(path.name)
    if not m:
        return None, None
    return m.group(1), m.group(2)


def parse_args():
    parser = argparse.ArgumentParser(description='Localization consistency check')
    parser.add_argument('--all', action='store_true', help='scan all source files, not only staged ones')
    return parser.parse_args()


def main():
    args = parse_args()
    root = repo_root()
    if root is None:
        return 1

    resx = resx_files(root)
    if not resx:
        print('No .resx files found; nothing to check.')
        return 0

    all_keys = set()
    for r in resx:
        all_keys.update(resx_keys(r))

    # Part 3: validate resx headers in all files
    header_errors = []
    for r in resx:
        errors = resx_header_errors(r)
        if errors:
            header_errors.append((r.relative_to(root), errors))

    # Part 1: check source files for missing keys
    if args.all:
        files = all_source_files(root)
        scan_mode = 'all'
    else:
        files = staged_files()
        scan_mode = 'staged'
    source_files = [f for f in files if f.endswith(('.cs', '.razor', '.cshtml'))]
    used_keys = set()
    missing = []
    for rel in source_files:
        path = root / rel
        if not path.exists():
            continue
        try:
            text = path.read_text(encoding='utf-8-sig', errors='ignore')
        except OSError:
            continue
        for pat in KEY_PATTERNS:
            for m in pat.finditer(text):
                key = m.group('key')
                used_keys.add(key)
                if key not in all_keys:
                    missing.append((rel, key))

    # Part 2: check resx package consistency
    packages = {}
    for r in resx:
        base, _ = resx_base(r)
        if base is None:
            continue
        package_key = (str(r.parent.relative_to(root).as_posix()), base)
        packages.setdefault(package_key, []).append(r)

    package_errors = []
    for _, files in packages.items():
        union = set()
        keys_per_file = {}
        for f in files:
            keys = resx_keys(f)
            keys_per_file[f] = keys
            union |= keys
        for f in files:
            missing_keys = union - keys_per_file[f]
            if missing_keys:
                package_errors.append((f.relative_to(root), missing_keys))

    failed = False
    if missing:
        failed = True
        print('ERROR: the following localization keys are used in staged files but not defined in any .resx:')
        for rel, key in sorted(set(missing)):
            print(f'  {rel}: {key}')
        print()

    if package_errors:
        failed = True
        print('ERROR: the following .resx files are missing keys that exist in other language files of the same package:')
        for f, missing_keys in package_errors:
            for key in sorted(missing_keys):
                print(f'  {f}: {key}')
        print()

    if header_errors:
        failed = True
        print('ERROR: the following .resx files have invalid headers:')
        for f, errors in header_errors:
            for e in errors:
                print(f'  {f}: {e}')
        print()

    if failed:
        return 1

    print(f'OK: {len(used_keys)} {scan_mode} localization key(s) found, {len(resx)} .resx package(s) are consistent and all resx headers are valid.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
