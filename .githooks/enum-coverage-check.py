#!/usr/bin/env python3
"""Enum test coverage check for C# source code.

For each public/internal enum in a solution, checks whether all of its
values appear in at least one test file:

  - If no test file references the enum type at all:
    -> error: no tests found for this enum type.
  - If a test file is found:
    -> every enum value must appear in at least one test file.

Test projects are recognized by directory name (suffix "Test"/"Tests",
case-insensitive). Private enums are not checked. A solution with no test
projects yet has nothing to check.

By default (as used in pre-commit) this only WARNS and always exits 0, so
work in progress is allowed while a feature is being built. Pass --strict
(as used in pre-push) to turn findings into a hard failure (exit 1), so
untested enum values cannot leave the machine.

Runs whenever a .cs file is staged (or always, with --all); the audit
itself always covers the whole solution/repository, since coverage is a
project-wide property, not a per-file one.
"""
import argparse
import re
import subprocess
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
sys.stderr.reconfigure(encoding='utf-8', errors='replace')

EXCLUDED_DIRS = {'.git', 'bin', 'obj', 'TestResults', 'node_modules', '.vs', '.idea', 'packages'}

ENUM_RE = re.compile(
    r'\b(?:public|internal)(?:\s+\w+)*\s+enum\s+(\w+)\s*(?::\s*[\w.]+)?\s*\{([^}]*)\}',
    re.DOTALL,
)


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
        files.append(p)
    return files


def find_solution_root(start_dir, root):
    """Walks upward from start_dir looking for a directory with a .sln file, bounded at root."""
    current = start_dir
    while True:
        try:
            entries = list(current.iterdir())
        except OSError:
            entries = []
        if any(e.is_file() and e.suffix == '.sln' for e in entries):
            return current
        if current == root:
            return root
        current = current.parent


def is_test_path(path, solution_root):
    rel = path.relative_to(solution_root)
    return any(p.lower().endswith(('test', 'tests')) for p in rel.parts[:-1])


def parse_enums(content):
    """Returns list of (enum_name, [values]) — only public/internal enums."""
    content = re.sub(r"//[^\n]*", "", content)
    content = re.sub(r"/\*.*?\*/", "", content, flags=re.DOTALL)

    enums = []
    for match in ENUM_RE.finditer(content):
        enum_name = match.group(1)
        body = match.group(2)
        values = []
        for part in body.split(","):
            part = re.sub(r"\[.*?\]", "", part).strip()  # strip [Attribute]
            value_match = re.match(r"^(\w+)", part)
            if value_match:
                values.append(value_match.group(1))
        if values:
            enums.append((enum_name, values))
    return enums


def check_solution(solution_root, files):
    source_files = [f for f in files if not is_test_path(f, solution_root)]
    test_files = [f for f in files if is_test_path(f, solution_root)]

    # Nothing to check if the solution has no test projects yet
    if not test_files:
        return []

    test_contents = {}
    for tf in test_files:
        try:
            test_contents[tf] = tf.read_text(encoding='utf-8', errors='replace')
        except OSError:
            pass

    all_enums = []
    for src_file in source_files:
        try:
            content = src_file.read_text(encoding='utf-8', errors='replace')
        except OSError:
            continue
        for enum_name, values in parse_enums(content):
            all_enums.append((enum_name, values, src_file))

    errors = []
    for enum_name, values, src_file in all_enums:
        rel_src = src_file.relative_to(solution_root).as_posix()
        relevant_tests = [tf for tf, content in test_contents.items() if enum_name in content]

        if not relevant_tests:
            errors.append(f'  Keine Tests für {enum_name} gefunden (definiert in {rel_src})')
            continue

        missing = [v for v in values if not any(v in test_contents[tf] for tf in relevant_tests)]
        if missing:
            errors.append(
                f'  {enum_name}: Enum-Werte nicht in Tests abgedeckt: {", ".join(missing)} ({rel_src})'
            )

    return errors


def find_all_issues(root):
    files = all_cs_files(root)
    groups = {}
    for f in files:
        sol_root = find_solution_root(f.parent, root)
        groups.setdefault(sol_root, []).append(f)

    issues = []
    for sol_root, group_files in groups.items():
        issues.extend(check_solution(sol_root, group_files))
    return issues


def parse_args():
    parser = argparse.ArgumentParser(description='Enum test coverage check')
    parser.add_argument('--all', action='store_true', help='run the audit unconditionally, not only when .cs files are staged')
    parser.add_argument('--strict', action='store_true', help='exit 1 on findings instead of only warning')
    return parser.parse_args()


def main():
    args = parse_args()
    root = repo_root()
    if root is None:
        return 1

    if not args.all:
        staged_cs = [f for f in staged_files() if f.endswith('.cs')]
        if not staged_cs:
            print('OK: no staged .cs files, nothing to check.')
            return 0

    issues = find_all_issues(root)

    if issues:
        level = 'ERROR' if args.strict else 'WARNING'
        print(f'{level}: unvollständige Enum-Testabdeckung:')
        print('\n'.join(issues))
        print('  -> Alle public/internal Enum-Werte müssen in mindestens einer Testdatei vorkommen.')
        if args.strict:
            return 1
        print('(Nur Warnung beim Commit — muss vor dem Push vollständig abgedeckt sein.)')
        return 0

    print('OK: all public/internal enums are covered by tests.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
