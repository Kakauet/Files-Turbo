"""Summarizes FilesBench JSON-lines output: median, p95 and count of valid samples per app and case.

Usage: python summarize.py <output-directory> [more directories...]
"""
import json
import math
import sys
from collections import defaultdict
from pathlib import Path

METRICS = {
    "navigation": ("Folder", ["FirstRowMs", "TenRowsMs", "AllRowsMs", "ListingCompleteMs", "CpuMs", "PrivateMiB"]),
    "operations": ("Operation", ["TotalMs", "CpuMs"]),
    "idle": ("Phase", ["CpuPercentOfOneCore", "WorkingSetMiB", "PrivateMiB"]),
    "startup": ("Case", ["FirstWindowMs", "FirstRowMs", "AllRowsMs"]),
    "search": ("Query", ["FirstResultMs", "CompleteMs"]),
    "thumbnails": ("Folder", ["FirstThumbnailMs", "AllThumbnailsMs"]),
}


def percentile(values, p):
    values = sorted(values)
    if not values:
        return math.nan
    k = (len(values) - 1) * p
    lo, hi = math.floor(k), math.ceil(k)
    return values[lo] + (values[hi] - values[lo]) * (k - lo)


def summarize(path):
    kind = path.stem
    if kind not in METRICS:
        return
    key, metrics = METRICS[kind]
    groups = defaultdict(list)
    invalid = defaultdict(int)
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if not line.strip():
            continue
        row = json.loads(line)
        group = (str(row.get(key, "")), row["App"])
        if row.get("Valid", True):
            groups[group].append(row)
        else:
            invalid[group] += 1
    present = [m for m in metrics if any(m in r for rows in groups.values() for r in rows)]
    print(f"\n## {path}\n")
    print("| " + " | ".join([key, "App", "n", "invalid"] + [f"{m} median / p95" for m in present]) + " |")
    print("|" + "---|" * (4 + len(present)))
    for (case, app), rows in sorted(groups.items()):
        cells = [case, app, str(len(rows)), str(invalid[(case, app)])]
        for m in present:
            values = [r[m] for r in rows if isinstance(r.get(m), (int, float))]
            cells.append(f"{percentile(values, 0.5):.1f} / {percentile(values, 0.95):.1f}" if values else "-")
        print("| " + " | ".join(cells) + " |")


for directory in sys.argv[1:]:
    for file in sorted(Path(directory).glob("*.jsonl")):
        summarize(file)
