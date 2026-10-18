#!/usr/bin/env python3
"""Behavior-preserving first-pass splitter for the SmartPosition cBot.

Moves top-level methods into partial-class files without rewriting method bodies.
The original source is never overwritten. This is a transitional mechanical split,
not the final service-based architecture.
"""
from __future__ import annotations

import argparse
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

CLASS_NAME = "SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume"

GROUPS: dict[str, str] = {
    # Robot lifecycle and event wiring
    "OnStart": "Lifecycle", "OnStop": "Lifecycle", "OnBar": "Lifecycle",
    "OnTick": "Lifecycle", "OnTimer": "Lifecycle",
    "OnLowerTimeframeBarOpened": "Lifecycle", "OnPositionOpened": "Lifecycle",
    "OnPositionClosed": "Lifecycle", "GetSelectedLowerTimeFrame": "Lifecycle",
    "ValidateParameters": "Lifecycle",
    # Entry signal generation and scoring
    "TryOpenHybridPosition": "EntryEngine", "GetHybridEntrySignal": "EntryEngine",
    "GetM5TrendBias": "EntryEngine", "GetLowerTimeframeSignal": "EntryEngine",
    "GetLowerMicroSwingBreakoutSignal": "EntryEngine",
    "IsConfirmedLowerStructureBreak": "EntryEngine",
    "GetLowerStructuralStopAnchor": "EntryEngine", "GetPriorLowerSwingLevel": "EntryEngine",
    "GetLowerAtrValue": "EntryEngine", "GetM5DirectionalStrengthScore": "EntryEngine",
    "GetOpportunityQualityScore": "EntryEngine", "GetLowerSignalQualityScore": "EntryEngine",
    "GetEntryTargetR": "EntryEngine", "GetNearestLowerStructuralBarrier": "EntryEngine",
    "GetNearestForwardBarrier": "EntryEngine",
    # Broker margin, sizing and risk budget
    "GetEstimatedMarginSafe": "RiskAndSizing",
    "GetMaximumVolumeWithinMargin": "RiskAndSizing",
    "GetAdaptiveAllowedRiskPercent": "RiskAndSizing",
    "EstimateRoundTurnCommissionPips": "RiskAndSizing",
    # Position lifecycle and identity scoping
    "ManageCurrentSymbolPositions": "PositionManagement",
    "IsThisBotPosition": "PositionManagement", "ShouldManage": "PositionManagement",
    "ManagePosition": "PositionManagement", "IsPositionStillOpen": "PositionManagement",
    # Initial stop, emergency handling, BE/trailing and stop geometry
    "AlignFilledPositionStopToStructure": "Protection", "TrySetInitialStop": "Protection",
    "TryCloseBreachedPosition": "Protection", "RetryInitialStop": "Protection",
    "SubmitInitialStop": "Protection", "ManageBreakEvenAndTrailing": "Protection",
    "HasRequiredCompletedBarsAfterEntry": "Protection",
    "GetLowerTimeframeDuration": "Protection", "IsStopOnRiskSide": "Protection",
    "GetMinimumStopDistance": "Protection", "NormalizeStopPrice": "Protection",
    "MoreProtectiveStop": "Protection",
    # Visual target guide only
    "CleanupPreviousTpGuideObjects": "SuggestedTargetGuide",
    "GetSuggestedTpLineName": "SuggestedTargetGuide",
    "GetSuggestedTpLabelName": "SuggestedTargetGuide",
    "RemoveSuggestedTpGuide": "SuggestedTargetGuide",
    "NormalizeSuggestedTpPrice": "SuggestedTargetGuide",
    "GetAdaptiveSuggestedTpR": "SuggestedTargetGuide",
    "GetNearestStructuralBarrier": "SuggestedTargetGuide",
    "UpdateSuggestedTpGuide": "SuggestedTargetGuide",
    "GetKnownIntradayBarDuration": "SuggestedTargetGuide",
    # State restore and persistence
    "GetOrCreateState": "PositionStateStore", "SaveState": "PositionStateStore",
    "GetStateKey": "PositionStateStore", "GetOwnerKey": "PositionStateStore",
    # Cross-instance lease coordination
    "ReloadSharedStorageIfNeeded": "OwnershipCoordinator",
    "TryAcquireManagementLease": "OwnershipCoordinator", "WriteLease": "OwnershipCoordinator",
    "TryReadLease": "OwnershipCoordinator",
    # On-chart volume controls
    "CreateVolumeControlPanel": "VolumeControlPanel",
    "ApplyVolumePanelValues": "VolumeControlPanel",
    "TryParsePanelNumber": "VolumeControlPanel",
    "UpdateVolumeControlPanelStatus": "VolumeControlPanel",
    "SetVolumeNotice": "VolumeControlPanel",
    # Shared helpers
    "Diag": "RuntimeHelpers", "GetAtrValue": "RuntimeHelpers",
    "Clamp01": "RuntimeHelpers", "IsFinitePositive": "RuntimeHelpers",
    "WarnThrottled": "RuntimeHelpers",
}

METHOD_START = re.compile(
    r"(?m)^[ \t]{8}(?:(?:public|protected|private|internal)\s+)"
    r"(?:(?:static|override|virtual|async|new|sealed|extern|unsafe)\s+)*"
    r"(?:[A-Za-z_][\w.<>,\[\]?]*\s+)+(?P<name>[A-Za-z_]\w*)\s*\("
)


def matching_delimiter(text: str, start: int, opening: str, closing: str) -> int:
    """Return matching delimiter index while ignoring C# comments and literals."""
    if start >= len(text) or text[start] != opening:
        raise ValueError(f"Expected {opening!r} at offset {start}")
    depth, state, i = 0, "code", start
    while i < len(text):
        ch = text[i]
        nxt = text[i + 1] if i + 1 < len(text) else ""
        if state == "code":
            if ch == "/" and nxt == "/": state = "line_comment"; i += 2; continue
            if ch == "/" and nxt == "*": state = "block_comment"; i += 2; continue
            if ch == "@" and nxt == '"': state = "verbatim_string"; i += 2; continue
            if ch == '"': state = "string"; i += 1; continue
            if ch == "'": state = "char"; i += 1; continue
            if ch == opening: depth += 1
            elif ch == closing:
                depth -= 1
                if depth == 0: return i
        elif state == "line_comment":
            if ch == "\n": state = "code"
        elif state == "block_comment":
            if ch == "*" and nxt == "/": state = "code"; i += 2; continue
        elif state == "string":
            if ch == "\\": i += 2; continue
            if ch == '"': state = "code"
        elif state == "verbatim_string":
            if ch == '"' and nxt == '"': i += 2; continue
            if ch == '"': state = "code"
        elif state == "char":
            if ch == "\\": i += 2; continue
            if ch == "'": state = "code"
        i += 1
    raise ValueError(f"Unbalanced {opening}{closing} starting at offset {start}")


def find_class_bounds(source: str, class_name: str) -> tuple[int, int, int]:
    pattern = re.compile(r"\bpublic\s+class\s+" + re.escape(class_name) + r"\b")
    matches = list(pattern.finditer(source))
    if len(matches) != 1:
        raise ValueError(f"Expected one public class {class_name}, found {len(matches)}")
    class_start = matches[0].start()
    opening = source.find("{", matches[0].end())
    if opening < 0:
        raise ValueError("Class opening brace not found")
    return class_start, opening, matching_delimiter(source, opening, "{", "}")


def find_body_start(source: str, close_paren: int) -> int:
    """Find the method body brace, rejecting syntax this reviewed parser does not support."""
    i, state = close_paren + 1, "code"
    while i < len(source):
        ch = source[i]
        nxt = source[i + 1] if i + 1 < len(source) else ""
        if state == "code":
            if ch.isspace(): i += 1; continue
            if ch == "/" and nxt == "/": state = "line_comment"; i += 2; continue
            if ch == "/" and nxt == "*": state = "block_comment"; i += 2; continue
            if source.startswith("where ", i):
                raise ValueError("Generic method constraints found; update splitter first")
            if ch == "{": return i
            if ch == "=" and nxt == ">":
                raise ValueError("Expression-bodied method found; update splitter first")
            if ch == ";": raise ValueError("Declaration-only method found; update splitter first")
            if ch not in "\r\n":
                raise ValueError(f"Unexpected token after method parameters at offset {i}: {source[i:i+30]!r}")
            i += 1
        elif state == "line_comment":
            if ch == "\n": state = "code"
            i += 1
        elif state == "block_comment":
            if ch == "*" and nxt == "/": state = "code"; i += 2
            else: i += 1
    raise ValueError("Method body brace not found")


def locate_methods(source: str, body_open: int, body_close: int) -> list[dict[str, object]]:
    methods = []
    for match in METHOD_START.finditer(source, body_open + 1, body_close):
        name = match.group("name")
        paren = source.find("(", match.start(), match.end())
        close_paren = matching_delimiter(source, paren, "(", ")")
        body_start = find_body_start(source, close_paren)
        body_end = matching_delimiter(source, body_start, "{", "}") + 1
        if body_end > body_close:
            raise ValueError(f"Method {name} extends outside class bounds")
        methods.append({"name": name, "start": match.start(), "end": body_end,
                        "text": source[match.start():body_end]})
    return methods


def split_source(source: str, class_name: str) -> tuple[str, dict[str, str], dict[str, int]]:
    _, body_open, body_close = find_class_bounds(source, class_name)
    methods = locate_methods(source, body_open, body_close)
    if not methods:
        raise ValueError("No methods found; source differs from the reviewed baseline")
    names = Counter(str(method["name"]) for method in methods)
    unknown = sorted(set(names) - set(GROUPS))
    missing = sorted(set(GROUPS) - set(names))
    if unknown or missing:
        details = []
        if unknown: details.append("unmapped methods: " + ", ".join(unknown))
        if missing: details.append("expected methods not found: " + ", ".join(missing))
        raise ValueError("Source inventory mismatch; " + "; ".join(details))

    groups: dict[str, list[dict[str, object]]] = defaultdict(list)
    for method in methods:
        groups[GROUPS[str(method["name"])]].append(method)

    primary = source
    for method in sorted(methods, key=lambda item: int(item["start"]), reverse=True):
        start, end = int(method["start"]), int(method["end"])
        primary = primary[:start] + primary[end:]
    primary = re.sub(
        r"\bpublic\s+class\s+" + re.escape(class_name) + r"\b",
        "public partial class " + class_name, primary, count=1,
    )

    usings = "\n".join(line for line in source.splitlines() if line.startswith("using "))
    partials: dict[str, str] = {}
    for group_name, group_methods in groups.items():
        method_text = "\n\n".join(str(item["text"]).rstrip() for item in group_methods)
        partials[f"SmartPositionAutoTrader.{group_name}.cs"] = (
            usings + "\n\nnamespace cAlgo.Robots\n{\n"
            f"    public partial class {class_name}\n    {{\n{method_text}\n    }}\n}}\n"
        )
    counts = {name: len(items) for name, items in groups.items()}
    counts["_total_methods"] = len(methods)
    if sum(counts[name] for name in groups) != len(methods):
        raise ValueError("Internal member-count mismatch; no output written")
    return primary, partials, counts


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="Path to the untouched original .cs file")
    parser.add_argument("--output-dir", type=Path, required=True, help="New output directory")
    parser.add_argument("--class-name", default=CLASS_NAME)
    parser.add_argument("--force", action="store_true", help="Permit writing into a non-empty output directory")
    args = parser.parse_args()
    if not args.source.is_file(): parser.error(f"Source file does not exist: {args.source}")
    source = args.source.read_text(encoding="utf-8-sig")
    try:
        primary, partials, counts = split_source(source, args.class_name)
    except ValueError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2
    out = args.output_dir
    if out.exists() and any(out.iterdir()) and not args.force:
        print(f"ERROR: output directory is not empty: {out}", file=sys.stderr)
        return 2
    out.mkdir(parents=True, exist_ok=True)
    (out / "SmartPositionAutoTrader.Robot.cs").write_text(primary, encoding="utf-8")
    for filename, content in partials.items():
        (out / filename).write_text(content, encoding="utf-8")
    print(f"Wrote {1 + len(partials)} files to {out}")
    for group, count in sorted(counts.items()): print(f"{group}: {count}")
    print("Mechanical partial-class split only. The input is unchanged; compile and review before behavior changes.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
