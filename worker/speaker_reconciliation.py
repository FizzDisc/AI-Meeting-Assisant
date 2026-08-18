"""Deterministic speaker assignment for source-separated transcript segments."""
from __future__ import annotations
from typing import Any

MIN_OVERLAP_RATIO = 0.5
AMBIGUITY_MARGIN = 0.15

def reconcile(segments: list[dict[str, Any]], turns: list[dict[str, Any]]) -> list[dict[str, Any]]:
    result = []
    for segment in segments:
        item = dict(segment)
        if item.get("source") == "microphone":
            item.update(speaker="You", speakerAssignment="known-source", speakerOverlapRatio=1.0)
            result.append(item); continue
        if item.get("source") != "system_audio":
            item.update(speaker=None, speakerAssignment="unassigned", speakerOverlapRatio=0.0)
            result.append(item); continue
        duration = max(float(item["end"]) - float(item["start"]), 0.001)
        overlaps: dict[str, float] = {}
        for turn in turns:
            overlap = max(0.0, min(float(item["end"]), float(turn["end"])) - max(float(item["start"]), float(turn["start"])))
            if overlap: overlaps[str(turn["speaker"])] = overlaps.get(str(turn["speaker"]), 0.0) + overlap
        ranked = sorted(overlaps.items(), key=lambda pair: (-pair[1], pair[0]))
        best_ratio = ranked[0][1] / duration if ranked else 0.0
        second_ratio = ranked[1][1] / duration if len(ranked) > 1 else 0.0
        if not ranked or best_ratio < MIN_OVERLAP_RATIO:
            speaker, assignment = None, "unassigned"
        elif len(ranked) > 1 and best_ratio - second_ratio < AMBIGUITY_MARGIN:
            speaker, assignment = None, "ambiguous"
        else:
            speaker, assignment = ranked[0][0], "assigned"
        item.update(speaker=speaker, speakerAssignment=assignment,
                    speakerOverlapRatio=round(best_ratio, 4),
                    speakerCandidates=[{"speaker": name, "overlapRatio": round(value / duration, 4)} for name, value in ranked])
        result.append(item)
    return result
