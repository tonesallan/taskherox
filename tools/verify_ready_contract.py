from __future__ import annotations

import ast
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PYTHON_SOURCE = ROOT / "python_old_project" / "tbh_core.py"
CSHARP_SOURCE = ROOT / "src" / "TaskHeroX.Core" / "Il2Cpp" / "Il2CppOffsetCache.cs"


def stop(message: str) -> None:
    raise SystemExit(f"[STOP] {message}")


def python_critical_symbols(source: str) -> list[str]:
    try:
        tree = ast.parse(source, filename=str(PYTHON_SOURCE))
    except SyntaxError as exc:
        stop(f"legacy Python syntax error: {exc}")

    for node in tree.body:
        if not isinstance(node, ast.Assign):
            continue
        if not any(isinstance(target, ast.Name) and target.id == "_CRIT_SYMS" for target in node.targets):
            continue
        if not isinstance(node.value, (ast.Tuple, ast.List)):
            stop("_CRIT_SYMS must be a literal tuple/list")
        values: list[str] = []
        for item in node.value.elts:
            if not isinstance(item, ast.Constant) or not isinstance(item.value, str):
                stop("_CRIT_SYMS must contain only literal strings")
            values.append(item.value)
        return values

    stop("_CRIT_SYMS assignment not found")
    return []


def csharp_critical_symbols(source: str) -> list[str]:
    match = re.search(
        r"CriticalNumericSymbols\s*=\s*\[(.*?)\];",
        source,
        flags=re.DOTALL,
    )
    if match is None:
        stop("CriticalNumericSymbols initializer not found")

    values = re.findall(r'"([^"]+)"', match.group(1))
    if not values:
        stop("CriticalNumericSymbols is empty")
    return values


def main() -> None:
    python_source = PYTHON_SOURCE.read_text(encoding="utf-8-sig")
    csharp_source = CSHARP_SOURCE.read_text(encoding="utf-8-sig")

    try:
        tree = ast.parse(python_source, filename=str(PYTHON_SOURCE))
    except SyntaxError as exc:
        stop(f"legacy Python syntax error: {exc}")

    python_keys = python_critical_symbols(python_source)
    csharp_keys = csharp_critical_symbols(csharp_source)

    if len(python_keys) != len(set(python_keys)):
        stop("_CRIT_SYMS contains duplicates")
    if len(csharp_keys) != len(set(csharp_keys)):
        stop("CriticalNumericSymbols contains duplicates")

    if python_keys != csharp_keys:
        only_python = [key for key in python_keys if key not in csharp_keys]
        only_csharp = [key for key in csharp_keys if key not in python_keys]
        stop(
            "READY contract mismatch: "
            f"Python={len(python_keys)} C#={len(csharp_keys)} "
            f"only_python={only_python} only_csharp={only_csharp}"
        )

    selected_nodes: list[ast.stmt] = []
    for node in tree.body:
        if isinstance(node, ast.Assign) and any(
            isinstance(target, ast.Name) and target.id == "_CRIT_SYMS"
            for target in node.targets
        ):
            selected_nodes.append(node)
        elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)) and node.name in {
            "_offsets_ok",
            "_missing_syms",
        }:
            selected_nodes.append(node)

    namespace: dict[str, object] = {}
    isolated = ast.Module(body=selected_nodes, type_ignores=[])
    ast.fix_missing_locations(isolated)
    exec(compile(isolated, str(PYTHON_SOURCE), "exec"), {"__builtins__": __builtins__}, namespace)

    offsets_ok = namespace.get("_offsets_ok")
    missing_syms = namespace.get("_missing_syms")
    if not callable(offsets_ok) or not callable(missing_syms):
        stop("_offsets_ok/_missing_syms could not be isolated")

    valid = {key: 1 for key in python_keys}
    valid.update({"ra_class": "ra", "ynj": [1], "inv_klass_ti": 2})
    if not offsets_ok(valid):
        stop("_offsets_ok rejects the canonical valid contract")
    if missing_syms(valid):
        stop(f"_missing_syms reports a valid contract as incomplete: {missing_syms(valid)}")

    invalid_cases = [
        ("generic jgc key with zero value", {"jgc": 0}, "generic_jgc_forbidden"),
        ("boolean critical value", {"gra": True}, "gra"),
        ("negative critical value", {"gra": -1}, "gra"),
        ("oversized critical value", {"gra": 0x1_0000_0000}, "gra"),
        ("malformed ynj element", {"ynj": [1, "junk"]}, "ynj"),
        ("boolean inventory singleton", {"inv_klass_ti": True}, "inv_singleton"),
        ("negative inventory singleton", {"inv_klass_ti": -1}, "inv_singleton"),
    ]

    for name, patch, expected_missing in invalid_cases:
        candidate = dict(valid)
        candidate.update(patch)
        if offsets_ok(candidate):
            stop(f"_offsets_ok accepted invalid case: {name}")
        missing = missing_syms(candidate)
        if expected_missing not in missing:
            stop(f"_missing_syms missed {name}: expected {expected_missing}, got {missing}")

    bau_route = dict(valid)
    bau_route.pop("inv_klass_ti", None)
    bau_route["bau_ti"] = 3
    if not offsets_ok(bau_route):
        stop("_offsets_ok rejects valid bau_ti singleton route")

    print(
        f"[PASS] READY contract synced: {len(csharp_keys)} critical numeric keys; "
        "Python syntax and acceptance semantics valid"
    )


if __name__ == "__main__":
    main()
