"""Writes src/CanopyStudio.Core/Lua/lua_api_catalog.json.

The catalog is what Canopy Studio's Lua editor completes: the canopy API and
the events from Canopy's docs/LUA.md, the sandbox's Lua names, Lua's keywords,
and the game commands of Canopy's src/Canopy/lua_commands.h, both read from
the external/Canopy submodule. Rerun it after the submodule moves; a Studio
test fails while the catalog is behind them.

    python -I scripts/lua_api_catalog.py
"""

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CANOPY = ROOT / "external" / "Canopy"
LUA_DOC = CANOPY / "docs" / "LUA.md"
COMMANDS = CANOPY / "src" / "Canopy" / "lua_commands.h"
OUT = ROOT / "src" / "CanopyStudio.Core" / "Lua" / "lua_api_catalog.json"

KEYWORDS = (
    "and break do else elseif end false for function goto if in local nil not or repeat return then true "
    "until while"
).split()

TABLES = {
    "canopy": "Canopy's API for this mod.",
    "canopy.save": "Values kept with the save being played.",
    "canopy.items": "The mod's own custom items.",
    "canopy.flags": "The mod's own flags.",
}

# The sandbox's libraries, as Lua 5.3 has them, minus what the sandbox drops.
LIBRARIES = {
    "string": "byte char find format gmatch gsub len lower match rep reverse sub upper pack packsize unpack",
    "table": "concat insert move pack remove sort unpack",
    "math": "abs ceil cos deg exp floor fmod huge log max maxinteger min mininteger modf pi rad random sin sqrt "
            "tan tointeger type ult atan asin acos",
    "utf8": "char charpattern codepoint codes len offset",
    "coroutine": "create isyieldable resume running status wrap yield",
}

LIBRARY_DOCS = {
    "string": "Lua's string library, read only.",
    "table": "A copy of Lua's table library.",
    "math": "A copy of Lua's math library, without randomseed.",
    "utf8": "A copy of Lua's utf8 library.",
    "coroutine": "A copy of Lua's coroutine library.",
}

NAME = re.compile(r"([A-Za-z_][\w.]*)(\([^)]*\))?")
LINK = re.compile(r"\[([^\]]+)\]\([^)]*\)")


def plain(text):
    """Markdown inline text as plain text."""
    text = LINK.sub(r"\1", text)
    return text.replace("`", "").replace("**", "").strip()


def sections(lines, level):
    """(heading, body lines) for each heading of level in lines."""
    marker = "#" * level + " "
    found = []
    for line in lines:
        if line.startswith(marker):
            found.append((line[len(marker):].strip(), []))
        elif found and re.match(r"#{1,%d} " % level, line):
            found.append((None, []))
        elif found:
            found[-1][1].append(line)
    return [(heading, body) for heading, body in found if heading is not None]


def paragraphs(body):
    """The prose paragraphs of body, without code blocks, lists or tables."""
    result = []
    current = []
    fenced = False
    for line in body:
        if line.startswith("```"):
            fenced = not fenced
            continue
        if fenced or line.startswith(("- ", "|")) or not line.strip():
            if current:
                result.append(" ".join(current))
                current = []
            continue
        current.append(line.strip())
    if current:
        result.append(" ".join(current))
    return result


def bullets(body):
    return [line[2:].strip() for line in body if line.startswith("- ")]


def api_entries(doc_lines):
    start = doc_lines.index("## API")
    end = doc_lines.index("## The sandbox")
    entries = []
    for heading, body in sections(doc_lines[start:end], 3):
        names = list(NAME.finditer(heading))
        first = plain(paragraphs(body)[0]) if paragraphs(body) else ""
        for match in names:
            name, args = match.group(1), match.group(2)
            doc = first
            if len(names) > 1:
                leaf = name.rsplit(".", 1)[-1]
                for bullet in bullets(body):
                    if bullet.startswith("`" + leaf + "`"):
                        doc = plain(bullet)
                        break
            entries.append({
                "name": name,
                "kind": "function" if args else "field",
                "signature": name + args if args else None,
                "doc": doc,
            })
    return entries


def events(doc_lines):
    start = doc_lines.index("## Events")
    found = []
    for line in doc_lines[start:]:
        if line.startswith("## API"):
            break
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        if len(cells) == 3 and cells[0].startswith("`"):
            found.append({"name": cells[0].strip("`"), "kind": "event", "signature": None, "doc": plain(cells[1])})
    return found


def sandbox_globals(doc_lines):
    line = next(line for line in doc_lines if line.startswith("- **Lua:**"))
    names = re.findall(r"`([A-Za-z_]\w*)`", line)
    return [{"name": name, "kind": "global", "signature": None, "doc": "Lua's " + name + "."}
            for name in names if name != "print"] + [{
                "name": "print", "kind": "function", "signature": "print(...)",
                "doc": "Writes one line to Canopy.log, as canopy.log does."}]


def commands(header):
    def listed(name):
        body = re.search(r"constexpr const char\* " + name + r"\[\] = \{(.*?)\};", header, re.S)
        return re.findall(r'"(\w+)"', body.group(1))

    allowed = [{"name": name, "kind": "command", "signature": None,
                "doc": "A game command. Works only while canopy.in_game() is true; its arguments are the game's own."}
               for name in listed("kAllowed")]
    guarded = [{"name": name, "kind": "guarded", "signature": None,
                "doc": "A game command that writes the save or starts game scripts. Unsafe mods only."}
               for name in listed("kGuarded")]
    return allowed + guarded


def main():
    doc_lines = LUA_DOC.read_text(encoding="utf-8").splitlines()
    entries = []
    entries += [{"name": name, "kind": "table", "signature": None, "doc": doc} for name, doc in TABLES.items()]
    entries += api_entries(doc_lines)
    entries += events(doc_lines)
    entries += sandbox_globals(doc_lines)
    for library, members in LIBRARIES.items():
        entries.append({"name": library, "kind": "library", "signature": None, "doc": LIBRARY_DOCS[library]})
        for member in members.split():
            entries.append({"name": library + "." + member, "kind": "function", "signature": None,
                            "doc": "Lua's " + library + "." + member + "."})
    entries += [{"name": word, "kind": "keyword", "signature": None, "doc": "A Lua keyword."} for word in KEYWORDS]
    entries += commands(COMMANDS.read_text(encoding="utf-8"))

    seen = set()
    for entry in entries:
        key = (entry["kind"] == "event", entry["name"])
        if key in seen:
            sys.exit("Twice in the catalog: " + entry["name"])
        seen.add(key)
        if not entry["doc"]:
            sys.exit("No doc for " + entry["name"])

    catalog = {"version": 1, "entries": entries}
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(catalog, indent=1, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    kinds = {}
    for entry in entries:
        kinds[entry["kind"]] = kinds.get(entry["kind"], 0) + 1
    print(OUT.relative_to(ROOT), ", ".join(f"{count} {kind}" for kind, count in sorted(kinds.items())))


if __name__ == "__main__":
    main()
