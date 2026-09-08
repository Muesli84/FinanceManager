#!/usr/bin/env python3
"""XML documentation check for .csproj and .cs files.

Run as a pre-commit hook (default: only staged files) or with --all to scan
the entire repository.

.csproj: checks GenerateDocumentationFile, WarningsAsErrors, NoWarn etc. so
         that missing XML doc comments actually fail the build (CS1591).
.cs:     forbids #pragma warning disable for XML-doc warning codes and
         checks completeness of documented members (<param>, <typeparam>,
         <returns>, <response>).
"""
import argparse
import re
import subprocess
import sys
from pathlib import Path
from xml.etree import ElementTree as ET

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
sys.stderr.reconfigure(encoding='utf-8', errors='replace')

EXCLUDED_DIRS = {'.git', 'bin', 'obj', 'TestResults', 'node_modules', '.vs', 'packages'}

# All C# warning codes related to XML documentation
XML_DOC_CODES = {
    "CS1591",  # Missing XML comment for publicly visible type or member
    "CS1572",  # XML comment has a param tag for a parameter that does not exist
    "CS1573",  # Parameter has no matching param tag in the XML comment
    "CS1574",  # XML comment has a cref attribute that could not be resolved
    "CS1580",  # Invalid type for parameter in XML comment cref attribute
    "CS1581",  # Invalid return type in XML comment cref attribute
    "CS1584",  # XML comment has syntactically incorrect cref attribute
    "CS1587",  # XML comment is not placed on a valid language element
    "CS1589",  # Unable to include XML fragment
    "CS1590",  # Invalid XML include element
    "CS1592",  # Badly formed XML in included comments
    "CS1598",  # XML comment file could not be opened
}

DOC_PARAM_RE = re.compile(r'<param\s+name=["\'](\w+)["\']')
DOC_TYPEPARAM_RE = re.compile(r'<typeparam\s+name=["\'](\w+)["\']')
DOC_RETURNS_RE = re.compile(r'<(?:returns|value)\b')
DOC_RESPONSE_RE = re.compile(r'<response\s+code=["\'](\d+)["\']')
HTTP_METHOD_ATTR_RE = re.compile(
    r'\[(?:Http(?:Get|Post|Put|Delete|Patch|Head|Options)|Route)\b', re.IGNORECASE
)
PRODUCES_RESPONSE_ATTR_RE = re.compile(r'\[ProducesResponseType\b', re.IGNORECASE)
MODIFIER_RE = re.compile(
    r'\b(?:public|private|protected|internal|static|virtual|override|'
    r'abstract|async|sealed|extern|partial|new|readonly)\s+'
)
PRAGMA_RE = re.compile(r"#\s*pragma\s+warning\s+disable\b(.+)", re.IGNORECASE)


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


def all_source_files(root):
    files = []
    for ext in ('*.cs', '*.csproj'):
        for p in root.rglob(ext):
            if any(part in EXCLUDED_DIRS for part in p.parts):
                continue
            files.append(str(p.relative_to(root).as_posix()))
    return files


def parse_codes(text):
    """Splits a semicolon- or comma-separated warning code string into a set."""
    if not text:
        return set()
    return {c.strip().upper() for c in text.replace(";", ",").split(",") if c.strip()}


def find_nearest_csproj(start_dir):
    """Searches upward through the directory tree for the nearest .csproj file."""
    current = start_dir
    while True:
        try:
            entries = list(current.iterdir())
        except OSError:
            entries = []
        for entry in entries:
            if entry.is_file() and entry.suffix == '.csproj':
                return entry
        if current.parent == current:
            return None
        current = current.parent


def check_csproj_for_xmldoc(csproj_path):
    """Returns a list of problems (empty = all good)."""
    try:
        tree = ET.parse(csproj_path)
        root = tree.getroot()
    except ET.ParseError:
        return []

    generate_doc = False
    treat_all_as_errors = False
    all_no_warn = set()
    all_warnings_as_errors = set()
    all_warnings_not_as_errors = set()

    for pg in root.iter("PropertyGroup"):
        node = pg.find("GenerateDocumentationFile")
        if node is not None and (node.text or "").strip().lower() == "true":
            generate_doc = True
        node = pg.find("TreatWarningsAsErrors")
        if node is not None and (node.text or "").strip().lower() == "true":
            treat_all_as_errors = True
        node = pg.find("NoWarn")
        if node is not None:
            all_no_warn |= parse_codes(node.text)
        node = pg.find("WarningsAsErrors")
        if node is not None:
            all_warnings_as_errors |= parse_codes(node.text)
        node = pg.find("WarningsNotAsErrors")
        if node is not None:
            all_warnings_not_as_errors |= parse_codes(node.text)

    problems = []
    if not generate_doc:
        problems.append(
            "<GenerateDocumentationFile>true</GenerateDocumentationFile> fehlt oder ist nicht auf true gesetzt"
        )
    suppressed_in_no_warn = XML_DOC_CODES & all_no_warn
    if suppressed_in_no_warn:
        problems.append(
            "XML-Dokumentationswarnungen in <NoWarn> unterdrückt: "
            + ", ".join(sorted(suppressed_in_no_warn))
        )
    downgraded = XML_DOC_CODES & all_warnings_not_as_errors
    if downgraded:
        problems.append(
            "XML-Dokumentationswarnungen in <WarningsNotAsErrors> herabgestuft: "
            + ", ".join(sorted(downgraded))
        )
    cs1591_via_treat = treat_all_as_errors and "CS1591" not in all_warnings_not_as_errors
    cs1591_explicit = "CS1591" in all_warnings_as_errors
    if not cs1591_via_treat and not cs1591_explicit:
        problems.append(
            "CS1591 ist nicht als Fehler konfiguriert – "
            "<WarningsAsErrors>CS1591</WarningsAsErrors> oder "
            "<TreatWarningsAsErrors>true</TreatWarningsAsErrors> fehlt"
        )
    return problems


# ── Helpers for completeness check ─────────────────────────────────────────

def _simplify_generics(text):
    """Iteratively collapses nested <...> to <> for simpler parsing."""
    prev = None
    while prev != text:
        prev = text
        text = re.sub(r'<[^<>]*>', '<>', text)
    return text


def extract_param_names(decl):
    """Extracts parameter names from a method or constructor declaration."""
    simplified = _simplify_generics(decl)
    m = re.search(r'\((.+)\)', simplified, re.DOTALL)
    if not m:
        return []
    params_str = m.group(1)
    params_str = re.sub(r'\[[^\]]*\]', '', params_str)
    params_str = _simplify_generics(params_str)

    names = []
    for part in params_str.split(','):
        part = part.strip()
        if not part:
            continue
        part = re.split(r'\s*=\s*', part)[0].strip()
        part = re.sub(r'\b(?:ref|out|in|params)\s+', '', part).strip()
        words = part.split()
        if words:
            name = words[-1].strip('*&')
            if name and re.match(r'^@?[a-zA-Z_]\w*$', name):
                names.append(name.lstrip('@'))
    return names


def extract_type_param_names(decl):
    """
    Extracts generic type parameter names (T, TResult etc.) from the declaration.
    Only the member's own type parameters (MethodName<T>), not type arguments
    in the return type.
    """
    before_paren = decl.split('(')[0] if '(' in decl else decl
    stripped = MODIFIER_RE.sub('', before_paren).strip()
    m = re.search(r'\w+\s*<([^<>]+)>\s*$', stripped)
    if not m:
        return []
    names = []
    for tp in m.group(1).split(','):
        tp = tp.strip()
        if tp and re.match(r'^[A-Z]\w*$', tp):
            names.append(tp)
    return names


def get_return_type(decl):
    """
    Returns the return type, or None if it cannot be determined
    (e.g. constructor or fewer than two tokens before the parameter list).
    """
    stripped = MODIFIER_RE.sub('', decl).strip()
    simplified = _simplify_generics(stripped)
    before_paren = simplified.split('(')[0].strip() if '(' in simplified else ''
    if not before_paren:
        return None
    tokens = before_paren.split()
    if len(tokens) < 2:
        return None
    return ' '.join(tokens[:-1])


def is_non_void_return(decl):
    """True if the method has a return value worth documenting."""
    ret = get_return_type(decl)
    if not ret:
        return False
    ret = ret.strip()
    return ret not in ('void', 'Task', 'ValueTask')


def member_display_name(decl):
    """Short, readable member name derived from the declaration."""
    m = re.search(r'(\w+)\s*(?:<[^>]*>)?\s*\(', decl)
    if m:
        return m.group(1)
    words = decl.split()
    return words[-1] if words else decl[:40]


def extract_produces_response_codes(attr_lines):
    """Extracts HTTP status codes from [ProducesResponseType(...)] attributes."""
    codes = set()
    for attr in attr_lines:
        if not PRODUCES_RESPONSE_ATTR_RE.search(attr):
            continue
        for m in re.finditer(r'Status(\d{3})\w*', attr):
            codes.add(m.group(1))
        for m in re.finditer(r'(?<!\d)(\d{3})(?!\d)', attr):
            codes.add(m.group(1))
    return codes


def parse_documented_members(content):
    """
    Parses .cs file content and returns a list of dicts:
    {'doc': str, 'attrs': [str], 'decl': str, 'line': int, 'name': str}
    """
    lines = content.splitlines()
    members = []
    i = 0
    n = len(lines)

    while i < n:
        if not lines[i].strip().startswith('///'):
            i += 1
            continue

        doc_start = i + 1  # 1-based
        doc_lines = []
        while i < n and lines[i].strip().startswith('///'):
            doc_lines.append(lines[i].strip())
            i += 1

        while i < n and not lines[i].strip():
            i += 1

        attr_lines = []
        while i < n:
            s = lines[i].strip()
            if not s:
                i += 1
                continue
            if s.startswith('['):
                attr_lines.append(s)
                i += 1
            else:
                break

        decl_lines = []
        paren_depth = 0
        found_paren = False
        limit = min(i + 15, n)
        j = i
        while j < limit:
            line = lines[j].strip()
            if not line or line.startswith('//'):
                break
            decl_lines.append(line)
            open_p = line.count('(')
            close_p = line.count(')')
            paren_depth += open_p - close_p
            if open_p > 0:
                found_paren = True
            j += 1
            if found_paren and paren_depth <= 0:
                break
            if not found_paren and ('{' in line or ';' in line or '=>' in line):
                break
        i = j

        if doc_lines and decl_lines:
            decl_text = ' '.join(decl_lines)
            members.append({
                'doc': '\n'.join(doc_lines),
                'attrs': attr_lines,
                'decl': decl_text,
                'line': doc_start,
                'name': member_display_name(decl_text),
            })

    return members


def check_cs_xmldoc_completeness(content):
    """
    Checks completeness of XML comments in .cs file content.
    Returns a list of problem descriptions.
    """
    members = parse_documented_members(content)
    issues = []

    for member in members:
        doc = member['doc']
        attrs = member['attrs']
        decl = member['decl']
        line = member['line']
        label = f"Zeile {line} ({member['name']})"

        # Only check members that have a <summary> — otherwise CS1591 already applies
        if '<summary' not in doc:
            continue

        param_names = extract_param_names(decl)
        documented_params = set(DOC_PARAM_RE.findall(doc))
        missing_params = [p for p in param_names if p not in documented_params]
        if missing_params:
            issues.append(f"{label}: fehlende <param>-Tags für: {', '.join(missing_params)}")

        type_params = extract_type_param_names(decl)
        documented_type_params = set(DOC_TYPEPARAM_RE.findall(doc))
        missing_type_params = [tp for tp in type_params if tp not in documented_type_params]
        if missing_type_params:
            issues.append(f"{label}: fehlende <typeparam>-Tags für: {', '.join(missing_type_params)}")

        if '(' in decl and is_non_void_return(decl) and not DOC_RETURNS_RE.search(doc):
            issues.append(f"{label}: fehlendes <returns>-Tag")

        is_http_action = any(HTTP_METHOD_ATTR_RE.search(a) for a in attrs)
        if is_http_action:
            expected_codes = extract_produces_response_codes(attrs)
            documented_codes = set(DOC_RESPONSE_RE.findall(doc))
            missing_codes = expected_codes - documented_codes
            if missing_codes:
                issues.append(
                    f"{label}: fehlende <response code=\"...\">-Tags für "
                    f"HTTP-Statuscodes: {', '.join(sorted(missing_codes))}"
                )

    return issues


def check_cs_pragma_violations(content):
    """Returns a list of (lineno, source_line, code) for forbidden pragma disables."""
    violations = []
    for lineno, line in enumerate(content.splitlines(), 1):
        m = PRAGMA_RE.search(line)
        if not m:
            continue
        for raw in re.split(r"[,\s]+", m.group(1).strip()):
            if not raw:
                continue
            if raw.upper().startswith("CS"):
                candidate = raw.upper()
            elif raw.strip().isdigit():
                candidate = "CS" + raw.strip()
            else:
                candidate = raw.upper()
            if candidate in XML_DOC_CODES:
                violations.append((lineno, line.strip(), candidate))
    return violations


def parse_args():
    parser = argparse.ArgumentParser(description='csproj/.cs XML documentation check')
    parser.add_argument('--all', action='store_true', help='scan all .cs/.csproj files, not only staged ones')
    return parser.parse_args()


def main():
    args = parse_args()
    root = repo_root()
    if root is None:
        return 1

    if args.all:
        files = all_source_files(root)
        scan_mode = 'all'
    else:
        files = [f for f in staged_files() if f.endswith(('.cs', '.csproj'))]
        scan_mode = 'staged'

    cs_files = [f for f in files if f.endswith('.cs')]
    csproj_files = {f for f in files if f.endswith('.csproj')}

    failed = False
    checked = 0

    # ── .cs checks: pragma bans + completeness ──────────────────────────────
    for rel in cs_files:
        path = root / rel
        if not path.exists():
            continue
        checked += 1
        try:
            content = path.read_text(encoding='utf-8', errors='replace')
        except OSError:
            continue

        pragma_violations = check_cs_pragma_violations(content)
        if pragma_violations:
            failed = True
            print(f'ERROR: #pragma warning disable für XML-Dokumentationscodes in {rel}:')
            for lineno, src, code in pragma_violations:
                print(f'  Zeile {lineno}: {code} ({src})')
            print()

        completeness_issues = check_cs_xmldoc_completeness(content)
        if completeness_issues:
            failed = True
            print(f'ERROR: unvollständige XML-Dokumentation in {rel}:')
            for issue in completeness_issues:
                print(f'  {issue}')
            print()

        # the nearest .csproj also needs checking, even if it wasn't staged itself
        csproj_path = find_nearest_csproj(path.parent)
        if csproj_path is not None:
            csproj_files.add(str(csproj_path.relative_to(root).as_posix()))

    # ── .csproj checks ───────────────────────────────────────────────────────
    for rel in sorted(csproj_files):
        path = root / rel
        if not path.exists():
            continue
        checked += 1
        problems = check_csproj_for_xmldoc(path)
        if problems:
            failed = True
            print(f'ERROR: XML-Doc-Konfiguration in {rel} unvollständig:')
            for p in problems:
                print(f'  {p}')
            print()

    if failed:
        return 1

    print(f'OK: {checked} {scan_mode} .cs/.csproj file(s) checked, XML documentation is complete and enforced.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
