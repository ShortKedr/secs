"""Check GitHub-facing bilingual Markdown without network access or mutation."""

from collections import Counter
from html.parser import HTMLParser
from pathlib import Path
import os
import re
import subprocess
import sys
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
DOCUMENTS = [
    "README.md", "SECS.Benchmarks/README.md", "SpaceBattle.Game/README.md",
    "SpaceBattle.Web/README.md",
    "SpaceBattle.Game/Assets/PROVENANCE.md", "SpaceBattle.Adapters/README.md",
    "docs/SECS_OPTIMIZATION.md", "docs/benchmarks/optimization-methodology.md",
    "docs/space-battle/README.md",
    "docs/space-battle/SYSTEMS.md", "docs/space-battle/TACTICS.md", "docs/space-battle/ARMAMENT.md", "docs/space-battle/REINFORCEMENTS.md", "docs/space-battle/COVER.md", "docs/space-battle/PERFORMANCE_INVESTIGATION.md", "docs/space-battle/REPORT.md",
    "docs/space-battle/SPATIAL_OPTIMIZATION.md", "docs/space-battle/FLEETS.md", "docs/space-battle/PHYSICS.md",
    "tools/physics/README.md", "tools/performance/README.md", "tools/HardwareStudy/README.md",
    "docs/space-battle/HARDWARE_OPTIMIZATION.md",
    "tools/web/MONO_COMPILER_STUDY.md",
    "tools/HardwareStudy/LOCALITY_OPTIMIZATION.md",
    "docs/space-battle/DAMAGE.md",
    "docs/space-battle/TACTICS_V3.md",
    "docs/space-battle/IOS_WEB.md",
    "docs/space-battle/DEPLOYMENT.md",
    "tools/space-battle/scenes/README.md",
    "docs/promo/space-battle/README.md",
    "Benchmarks.Local/README.md", "THIRD_PARTY_NOTICES.md",
    "docs/README.md", "docs/secs/README.md", "docs/secs/INSTALL.md",
    "SpaceBattle.Game/DEVELOPMENT.md", "SpaceBattle.Web/DEVELOPMENT.md",
    "docs/space-battle/RESEARCH.md",
    "docs/space-battle/REPORT_2026-10-11.md", "docs/benchmarks/current/REPORT.md",
    "docs/space-battle/ARCHITECTURE.md",
    "docs/benchmarks/COLORS.md",
    "docs/benchmarks/current/SUPPLEMENT.md",
    "docs/space-battle/GAME_REPORT.md", "docs/space-battle/DESKTOP_TIMING.md",
]


def russian_path(path):
    return path.with_name(path.stem + ".ru.md")


def without_fences(text):
    lines, blocks = [], []
    fence = None
    current = []
    for line in text.splitlines():
        match = re.match(r"^ {0,3}(`{3,}|~{3,})(.*)$", line)
        if fence is None:
            if match:
                fence = match[1]
                current = [match[2]]
            else:
                lines.append(line)
        elif match and match[1][0] == fence[0] and len(match[1]) >= len(fence) and not match[2].strip():
            blocks.append("\n".join(current))
            current = []
            fence = None
        else:
            current.append(line)
    if fence:
        raise ValueError("Unclosed fenced code block")
    return "\n".join(lines), blocks


def inline_links(text):
    # Accept nested image/link labels and balanced parentheses in destinations.
    for match in re.finditer(r"(?<!\\)\]\(", text):
        start = match.end()
        depth, cursor = 1, start
        while cursor < len(text) and depth:
            if text[cursor] == "\\":
                cursor += 2
                continue
            if text[cursor] == "(":
                depth += 1
            elif text[cursor] == ")":
                depth -= 1
            cursor += 1
        if depth:
            raise ValueError("Unclosed Markdown link destination")
        destination = text[start:cursor - 1].strip()
        if destination.startswith("<"):
            destination = destination[1:destination.index(">")]
        else:
            destination = destination.split(maxsplit=1)[0]
        yield destination
    for match in re.finditer(r"^ {0,3}\[[^\]]+\]:\s*(?:<([^>]+)>|(\S+))", text, re.MULTILINE):
        yield match[1] or match[2]


class HtmlLinks(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links, self.ids = [], set()

    def handle_starttag(self, tag, attrs):
        for key, value in attrs:
            if key in ("href", "src") and value:
                self.links.append(value)
            if key in ("id", "name") and value:
                self.ids.add(value)


def anchors(text):
    clean, _ = without_fences(text)
    html = HtmlLinks()
    html.feed(clean)
    result, duplicates = set(html.ids), Counter()
    for line in clean.splitlines():
        match = re.match(r"^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$", line)
        if not match:
            continue
        label = re.sub(r"!?\[([^\]]*)\]\([^)]*\)", r"\1", match[1])
        label = re.sub(r"`+([^`]+?)`+", lambda m: m[1].replace("<", "").replace(">", ""), label)
        label = re.sub(r"<[^>]*>", "", label).lower()
        label = re.sub(r"[^\w\-\s]", "", label)
        slug = re.sub(r"\s", "-", label)
        number = duplicates[slug]
        duplicates[slug] += 1
        result.add(slug + ("-" + str(number) if number else ""))
    return result


def exact_case(path):
    relative = path.relative_to(ROOT)
    current = ROOT
    for part in relative.parts:
        names = {child.name for child in current.iterdir()}
        if part not in names:
            raise ValueError("Wrong path case or missing file: " + str(relative))
        current /= part


def table_numbers(text):
    clean, _ = without_fences(text)
    numbers = re.findall(r"(?<!\w)[+-]?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)*(?!\w)",
                         "\n".join(line for line in clean.splitlines() if line.startswith("|")))
    return Counter(n.replace(",", "") for n in numbers)


def check(root=ROOT):
    if root != ROOT:
        raise ValueError("This checker validates the current project only")
    paths = [ROOT / name for name in DOCUMENTS]
    targets, count = set(), 0
    for english in paths:
        russian = russian_path(english)
        en = english.read_text(encoding="utf-8-sig")
        ru = russian.read_text(encoding="utf-8-sig")
        en_clean, en_code = without_fences(en)
        ru_clean, ru_code = without_fences(ru)
        if en_code != ru_code:
            raise ValueError("Translated executable snippets differ: " + str(english.relative_to(ROOT)))
        if table_numbers(en) != table_numbers(ru):
            raise ValueError("Translated numeric table values differ: " + str(english.relative_to(ROOT)))
        if not en.splitlines()[0].startswith("English | [Русский](" + russian.name + ")"):
            raise ValueError("Missing top Russian switch: " + str(english.relative_to(ROOT)))
        if not ru.splitlines()[0].startswith("[English](" + english.name + ") | Русский"):
            raise ValueError("Missing top English switch: " + str(russian.relative_to(ROOT)))
        if re.search("[А-Яа-яЁё]", en_clean.replace("Русский", "")):
            raise ValueError("Untranslated English prose: " + str(english.relative_to(ROOT)))
        for path, clean in ((english, en_clean), (russian, ru_clean)):
            html = HtmlLinks()
            html.feed(clean)
            for destination in [*inline_links(clean), *html.links]:
                parsed = urlsplit(destination)
                if parsed.scheme in ("https", "http", "mailto"):
                    continue
                if parsed.scheme or parsed.netloc or destination.startswith(("/", "\\")):
                    raise ValueError("Non-portable Markdown link: " + str(path.relative_to(ROOT)) + ": " + destination)
                lexical = Path(os.path.abspath(path.parent / unquote(parsed.path))) if parsed.path else path
                resolved = lexical.resolve()
                try:
                    resolved.relative_to(ROOT)
                except ValueError:
                    raise ValueError("Link escapes project: " + destination)
                if not resolved.exists():
                    raise ValueError("Broken link: " + str(path.relative_to(ROOT)) + ": " + destination)
                exact_case(lexical)
                if parsed.fragment and resolved.suffix == ".md":
                    fragment = unquote(parsed.fragment)
                    if fragment not in anchors(resolved.read_text(encoding="utf-8-sig")):
                        raise ValueError("Broken heading anchor: " + str(path.relative_to(ROOT)) + ": " + destination)
                targets.add(resolved.relative_to(ROOT).as_posix())
                count += 1
            if path.name.endswith(".ru.md"):
                for destination in inline_links(clean):
                    if destination.endswith(".md") and not destination.endswith(".ru.md"):
                        parsed = urlsplit(destination)
                        target = (path.parent / destination).resolve()
                        original_license = target.name == "LICENSE.md" and target.is_relative_to(ROOT / "Benchmarks.Local/vendor")
                        raw_evidence = target.name.endswith("-report-github.md") and target.parent.name == "results" and target.is_relative_to(ROOT / "Benchmarks.Local")
                        if not parsed.scheme and target != english and not original_license and not raw_evidence:
                            raise ValueError("Russian navigation leaves its locale: " + str(path.relative_to(ROOT)) + ": " + destination)
    # Check remaining single-language public guides without imposing a bilingual
    # template on them.
    paired = {p for en in paths for p in (en, russian_path(en))}
    single_paths = sorted(p for p in (ROOT / "docs/secs").rglob("*.md") if p not in paired)
    for path in single_paths:
        clean, _ = without_fences(path.read_text(encoding="utf-8-sig"))
        html = HtmlLinks()
        html.feed(clean)
        for destination in [*inline_links(clean), *html.links]:
            parsed = urlsplit(destination)
            if parsed.scheme in ("https", "http", "mailto"):
                continue
            if parsed.scheme or parsed.netloc or destination.startswith(("/", "\\")):
                raise ValueError("Non-portable Markdown link: " + str(path.relative_to(ROOT)) + ": " + destination)
            lexical = Path(os.path.abspath(path.parent / unquote(parsed.path))) if parsed.path else path
            resolved = lexical.resolve()
            if not resolved.is_relative_to(ROOT) or not resolved.exists():
                raise ValueError("Broken or external-project link: " + str(path.relative_to(ROOT)) + ": " + destination)
            exact_case(lexical)
            if parsed.fragment and resolved.suffix == ".md":
                if unquote(parsed.fragment) not in anchors(resolved.read_text(encoding="utf-8-sig")):
                    raise ValueError("Broken heading anchor: " + str(path.relative_to(ROOT)) + ": " + destination)
            targets.add(resolved.relative_to(ROOT).as_posix())
            count += 1
    git = Path.home() / ".cache/codex-runtimes/codex-primary-runtime/dependencies/native/git/cmd/git.exe"
    executable = str(git) if git.is_file() else "git"
    ignored = subprocess.run([executable, "check-ignore", "--stdin"], cwd=ROOT,
                             input=("\n".join(sorted(targets)) + "\n").encode("utf-8"), capture_output=True)
    if ignored.returncode not in (0, 1):
        raise ValueError("Could not verify publishable links: " + ignored.stderr.decode("utf-8", errors="replace"))
    if ignored.stdout.strip():
        raise ValueError("Linked files are ignored and will not be published: " + ignored.stdout.decode("utf-8", errors="replace"))
    for target in targets:
        path = ROOT / target
        if path.is_file() and path.stat().st_size >= 100 * 1024 ** 2:
            raise ValueError("Linked file exceeds GitHub's normal file limit: " + target)
    return len(paths) * 2 + len(single_paths), count, len(targets)


def main():
    try:
        documents, links, files = check()
    except (ValueError, OSError) as error:
        print("FAIL: " + str(error), file=sys.stderr)
        return 1
    print(f"PASS: {documents} documents; {links} local links/anchors; {files} publishable targets; bilingual code and numeric-table parity")
    return 0


if __name__ == "__main__":
    sys.exit(main())
