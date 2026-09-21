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

    print(f"[PASS] READY contract synced: {len(csharp_keys)} critical numeric keys; Python syntax valid")


if __name__ == "__main__":
    main()
