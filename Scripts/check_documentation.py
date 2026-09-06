#!/usr/bin/env python3

# Copyright (C) 2026 HyPrism Launcher
# SPDX-License-Identifier: GPL-3.0-only

"""Validate documentation prose, bilingual routes, and Core service contracts"""

from __future__ import annotations

from pathlib import Path
import re
import sys


ROOT = Path(__file__).resolve().parents[1]
CONTENT = ROOT / "Docs" / "content"
CORE = ROOT / "Sources" / "HyPrism.Core"
PROSE_FILES = [ROOT / "README.md", *sorted(CONTENT.rglob("*.mdx"))]
METHOD_PATTERN = re.compile(r"(?:public\s+)?(.+?)\s+(\w+)\((.*)\);$")
XML_END_TAGS = "summary|param|returns|exception|remarks"
FENCE_PATTERN = re.compile(r"^\s*(?P<marker>`{3,}|~{3,})(?P<info>.*)$")
IMAGE_PATTERN = re.compile(r"!\[(?P<alt>[^\]]*)\]\((?P<target>[^)]+)\)")
LINK_PATTERN = re.compile(r"(?<!!)\[[^\]]+\]\((?P<target>[^)]+)\)")
HEADING_ID_PATTERN = re.compile(r"^#{1,6} .+\{/\* #([\w-]+) \*/\}\s*$")


def prose_lines(path: Path):
    """Yield lines outside examples so sample links are not treated as real links"""
    marker = ""
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        fence = FENCE_PATTERN.match(line)
        if fence:
            candidate = fence.group("marker")
            if not marker:
                marker = candidate
            elif candidate[0] == marker[0] and len(candidate) >= len(marker) and not fence.group("info").strip():
                marker = ""
            continue
        if not marker:
            yield number, line


def check_prose() -> list[str]:
    errors: list[str] = []

    for path in PROSE_FILES:
        marker = ""
        opening_line = 0
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            stripped = line.rstrip()
            fence = FENCE_PATTERN.match(stripped)
            if fence:
                candidate = fence.group("marker")
                if not marker:
                    marker = candidate
                    opening_line = number
                    if not fence.group("info").strip():
                        errors.append(
                            f"{path.relative_to(ROOT)}:{number}: add a language to the code fence"
                        )
                elif candidate[0] == marker[0] and len(candidate) >= len(marker) and not fence.group("info").strip():
                    marker = ""
                continue
            if marker:
                continue
            if "—" in line:
                errors.append(f"{path.relative_to(ROOT)}:{number}: replace the em dash")
            if stripped.endswith("."):
                errors.append(f"{path.relative_to(ROOT)}:{number}: remove the final period")
        if marker:
            errors.append(f"{path.relative_to(ROOT)}:{opening_line}: unclosed code fence")

    return errors


def check_images() -> list[str]:
    errors: list[str] = []

    for path in sorted(CONTENT.rglob("*.mdx")):
        for number, line in prose_lines(path):
            for match in IMAGE_PATTERN.finditer(line):
                if not match.group("alt").strip():
                    errors.append(
                        f"{path.relative_to(ROOT)}:{number}: image alternative text is empty"
                    )

                target = match.group("target").split("#", 1)[0]
                if "://" in target or target.startswith("/"):
                    continue
                resolved = (path.parent / target).resolve()
                if not resolved.is_file():
                    errors.append(
                        f"{path.relative_to(ROOT)}:{number}: image does not exist: {target}"
                    )

    return errors


def check_links() -> list[str]:
    errors: list[str] = []
    for locale in ("en", "ru"):
        pages = {}
        anchors = {}
        for path in sorted((CONTENT / locale).rglob("*.mdx")):
            route = path.relative_to(CONTENT / locale).with_suffix("").as_posix()
            route = route.removesuffix("/index") if route != "index" else ""
            pages[route] = path
            anchors[route] = {
                match.group(1)
                for _, line in prose_lines(path)
                if (match := HEADING_ID_PATTERN.match(line))
            }

        for route, path in pages.items():
            for number, line in prose_lines(path):
                for match in LINK_PATTERN.finditer(line):
                    target = match.group("target")
                    location = f"{path.relative_to(ROOT)}:{number}"
                    if target.startswith("/docs/") or target == "/docs" or target.startswith("#"):
                        destination, _, anchor = target.partition("#")
                        destination_route = route if not destination else destination.removeprefix("/docs").strip("/")
                        if destination_route not in pages:
                            errors.append(f"{location}: missing documentation route: {target}")
                        elif anchor and anchor not in anchors[destination_route]:
                            errors.append(f"{location}: missing explicit bilingual heading ID: {target}")
                    elif target.startswith("../"):
                        resolved = (path.parent / target.split("#", 1)[0]).resolve()
                        if not resolved.is_relative_to(ROOT) or not resolved.exists():
                            errors.append(f"{location}: missing repository target: {target}")
    return errors


def check_language_parity() -> list[str]:
    errors: list[str] = []
    english = {
        path.relative_to(CONTENT / "en")
        for path in (CONTENT / "en").rglob("*.mdx")
    }
    russian = {
        path.relative_to(CONTENT / "ru")
        for path in (CONTENT / "ru").rglob("*.mdx")
    }

    for missing in sorted(english - russian):
        errors.append(f"Docs/content/ru/{missing}: Russian page is missing")
    for missing in sorted(russian - english):
        errors.append(f"Docs/content/en/{missing}: English page is missing")

    return errors


def split_parameters(raw: str) -> list[str]:
    parameters: list[str] = []
    depth = 0
    current: list[str] = []

    for character in raw:
        if character in "<[(":
            depth += 1
        elif character in ">])":
            depth -= 1
        if character == "," and depth == 0:
            parameters.append("".join(current))
            current = []
        else:
            current.append(character)

    if current:
        parameters.append("".join(current))
    return parameters


def preceding_xml(lines: list[str], index: int) -> str:
    comments: list[str] = []
    cursor = index - 1

    while cursor >= 0:
        stripped = lines[cursor].lstrip()
        if stripped.startswith("///"):
            comments.append(stripped)
        elif not stripped or stripped.startswith("["):
            pass
        else:
            break
        cursor -= 1

    return "\n".join(reversed(comments))


def check_core_contracts() -> list[str]:
    errors: list[str] = []

    for path in sorted(CORE.rglob("I*.cs")):
        lines = path.read_text(encoding="utf-8").splitlines()
        if not any("public interface " in line for line in lines):
            continue

        for index, line in enumerate(lines):
            if not line.lstrip().startswith("///"):
                continue
            location = f"{path.relative_to(ROOT)}:{index + 1}"
            if "—" in line:
                errors.append(f"{location}: replace the em dash")
            if re.search(rf"\.(?=</(?:{XML_END_TAGS}>)\s*$)", line):
                errors.append(f"{location}: remove the final period")
                continue
            if not line.rstrip().endswith("."):
                continue

            cursor = index + 1
            while cursor < len(lines) and lines[cursor].strip() == "///":
                cursor += 1
            if cursor < len(lines) and re.match(
                rf"^\s*///\s*</(?:{XML_END_TAGS})>\s*$",
                lines[cursor],
            ):
                errors.append(f"{location}: remove the final period")

        inside_interface = False
        for index, line in enumerate(lines):
            if "public interface " in line:
                inside_interface = True
            signature = line.strip()
            if not inside_interface or not signature.endswith(";") or "(" not in signature:
                continue
            if signature.startswith(("///", "event ")):
                continue

            match = METHOD_PATTERN.match(signature)
            if not match:
                continue

            return_type, method_name, raw_parameters = match.groups()
            documentation = preceding_xml(lines, index)
            location = f"{path.relative_to(ROOT)}:{index + 1}"

            if "<summary>" not in documentation:
                errors.append(f"{location}: {method_name} has no summary")

            for parameter in split_parameters(raw_parameters):
                declaration = parameter.split("=", 1)[0].strip()
                name_match = re.search(r"([A-Za-z_]\w*)\s*$", declaration)
                if name_match and f'<param name="{name_match.group(1)}"' not in documentation:
                    errors.append(
                        f"{location}: {method_name} has no param entry for {name_match.group(1)}"
                    )

            if return_type.strip() != "void" and "<returns>" not in documentation:
                errors.append(f"{location}: {method_name} has no returns entry")

    return errors


def main() -> int:
    errors = [
        *check_prose(),
        *check_images(),
        *check_links(),
        *check_language_parity(),
        *check_core_contracts(),
    ]
    if not errors:
        print("Documentation checks passed")
        return 0

    print("Documentation checks failed", file=sys.stderr)
    for error in errors:
        print(f"  {error}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
