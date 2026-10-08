# SPDX-License-Identifier: GPL-2.0-or-later
"""Aggregate PresentMon Console CSVs using the report's edge/chain selection.

Usage: python summarize-presentmon.py sample1.csv sample2.csv sample3.csv
"""
import collections
import csv
import json
import math
import statistics
import sys

rows = []
for filename in sys.argv[1:]:
    with open(filename, encoding="utf-8-sig", newline="") as handle:
        sample = list(csv.DictReader(handle))
    if not sample:
        raise SystemExit("No frames in " + filename)
    chain = collections.Counter(row["SwapChainAddress"] for row in sample).most_common(1)[0][0]
    rows.extend([row for row in sample if row["SwapChainAddress"] == chain][2:-2])
if not rows:
    raise SystemExit("Pass one or more nonempty PresentMon CSV files.")

def metric(key):
    values = sorted(float(row[key]) for row in rows
                    if row.get(key, "") not in ("", "NA") and math.isfinite(float(row[key])))
    if not values:
        return None
    return {"valid": len(values), "mean": statistics.mean(values),
            "median": statistics.median(values),
            "p95": values[min(len(values)-1, int(len(values)*.95))],
            "p99": values[min(len(values)-1, int(len(values)*.99))]}

frame = metric("MsBetweenPresents")
if not frame or frame["mean"] <= 0:
    raise SystemExit("No usable presentation interval.")
print(json.dumps({"frames": len(rows), "averageFps": 1000/frame["mean"],
                  "frameMs": frame, "gpuBusyMs": metric("MsGPUBusy"),
                  "gpuTimeMs": metric("MsGPUTime")}, indent=2))
