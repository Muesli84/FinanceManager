#!/usr/bin/env python3
"""Razor component usage check.

Warns (never blocks the commit) about orphaned Razor components: a .razor
file that is not a page/entry point and is not referenced by any other
.razor file in the same project.

By default the check only runs when at least one .razor file is staged
(nothing to warn about otherwise), but always audits the whole repository
so pre-existing orphans caused by the current change are still surfaced.
Pass --all to run the audit unconditionally (e.g. outside of a commit).

Exceptions (not checked):
  - Files with an @page directive (pages are entry points)
  - _Imports.razor (global imports)
  - App.razor, Routes.razor (app-level entry points)
  - Any file whose name starts with _
"""
import argparse
import re
import subprocess
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
sys.stderr.reconfigure(encoding='utf-8', errors='replace')

EXCLUDED_DIRS = {'.git', 'bin', 'obj', 'TestResults', 'node_modules', '.vs', 'packages'}
SKIP_NAMES = {"App", "Routes"}

TAG_TEMPLATE = r"<{}[\s/>@]"
LAYOUT_TEMPLATE = r"@layout\s+{}\b"
TYPEOF_TEMPLATE = r"typeof\s*\(\s*{}\s*\)"


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
        files.append(p)
    return files


def find_project_root(start_dir, root):
    """Walks upward from start_dir looking for a directory with a .csproj/.sln file, bounded at root."""
    current = start_dir
    while True:
        try:
            entries = list(current.iterdir())
        except OSError:
            entries = []
        if any(e.is_file() and e.suffix in ('.csproj', '.sln') for e in entries):
            return current
        if current == root:
            return root
        current = current.parent


def is_entry_point(path, content):
    """True if the file is an entry point and does not need to be referenced elsewhere."""
    name = path.stem
    if name.startswith("_"):
        return True
    if name in SKIP_NAMES:
        return True
    if re.search(r"^\s*@page\s+", content, re.MULTILINE):
        return True
    return False


def is_used_in_project(component_name, component_path, all_contents):
    """True if component_name is referenced as a tag, @layout, or typeof() in any other file."""
    tag_pattern = re.compile(TAG_TEMPLATE.format(re.escape(component_name)))
    layout_pattern = re.compile(LAYOUT_TEMPLATE.format(re.escape(component_name)))
    typeof_pattern = re.compile(TYPEOF_TEMPLATE.format(re.escape(component_name)))

    for path, content in all_contents.items():
        if path == component_path:
            continue
        if tag_pattern.search(content) or layout_pattern.search(content) or typeof_pattern.search(content):
            return True
    return False


def find_unused_components(root):
    razor_files = all_razor_files(root)

    file_contents = {}
    for f in razor_files:
        try:
            file_contents[f] = f.read_text(encoding='utf-8', errors='replace')
        except OSError:
            continue

    # Group files by their nearest project root so components are only
    # matched for usage within the same project.
    groups = {}
    for f in file_contents:
        proj_root = find_project_root(f.parent, root)
        groups.setdefault(proj_root, {})[f] = file_contents[f]

    unused = []
    for proj_root, contents in groups.items():
        for f, content in contents.items():
            if is_entry_point(f, content):
                continue
            component_name = f.stem
            # Razor components conventionally start with an uppercase letter
            if not component_name[0].isupper():
                continue
            if not is_used_in_project(component_name, f, contents):
                unused.append(str(f.relative_to(root).as_posix()))

    return sorted(unused)


def parse_args():
    parser = argparse.ArgumentParser(description='Razor component usage check')
    parser.add_argument('--all', action='store_true', help='run the audit unconditionally, not only when .razor files are staged')
    parser.add_argument('--strict', action='store_true', help='exit 1 on findings instead of only warning')
    return parser.parse_args()


def main():
    args = parse_args()
    root = repo_root()
    if root is None:
        return 1

    if not args.all:
        staged_razor = [f for f in staged_files() if f.endswith('.razor')]
        if not staged_razor:
            print('OK: no staged .razor files, nothing to check.')
            return 0

    unused = find_unused_components(root)

    if unused:
        level = 'ERROR' if args.strict else 'WARNING'
        print(f'{level}: possibly orphaned Razor components (not referenced anywhere in their project):')
        for u in unused:
            print(f'  {u}')
        print('  -> Reference the component, or delete it if it is no longer needed.')
        return 1 if args.strict else 0

    print('OK: no orphaned Razor components found.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
