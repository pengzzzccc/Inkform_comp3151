# -*- coding: utf-8 -*-
"""Scan a folder for Inkform performance-log CSVs and plot a normalized difficulty curve.

Every session is mapped onto the same axis length: the gameplay portion (everything
outside MainMenu/End) is stretched to 0-100% of session progress, so a 60-minute
clear and a 113-minute quit can be compared shape-to-shape.

Usage:
    python difficulty_curve.py [folder] [--out FILE.png] [--window 5]

    folder      folder to scan for *.csv (default: current directory, non-recursive)
    --out       output PNG path (default: <folder>/difficulty_curve.png)
    --window    rolling-rate window as percent of session span (default: 5)

Each CSV must be a PerformanceRecorder log: '#' comment lines, then a column header
line starting with "time_s". The time_s / scene / deaths columns are located by name,
so extra columns do not break parsing.

The session legend is placed outside the plot on the right so it never covers the
curves. Colors are sampled from a continuous colormap, so any number of CSVs gets
distinct colors (the 10-color default cycle would repeat past 10 files). Peak
annotations are only drawn for small batches (<= 10 sessions); with more they turn
into an unreadable wall of text.
"""

import argparse
import csv
import glob
import os
import sys

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

MENU_SCENES = {"MainMenu", "End"}   # not part of the gameplay span
GRID_STEP_PCT = 0.25                # rate evaluated every 0.25% of session progress
ANNOTATION_LIMIT = 10               # max sessions that still get peak annotations


def parse_session(path):
    """Parse one perf-log CSV.

    Returns a dict with:
      events  - normalized positions (0-100) of each death
      curve   - (position, deaths-so-far) points for the cumulative step line
      span    - real gameplay seconds (first to last gameplay row)
      total   - final death count
    or None if the file is not a usable perf log.
    """
    header = None
    rows = []
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            parts = next(csv.reader([line]))
            if header is None:
                if parts and parts[0].strip() == "time_s":
                    header = [p.strip() for p in parts]
                continue
            if len(parts) < len(header):
                continue
            rows.append(parts)
    if header is None or "deaths" not in header or "time_s" not in header:
        return None

    i_t = header.index("time_s")
    i_s = header.index("scene") if "scene" in header else 1
    i_d = header.index("deaths")

    game = []   # (time_s, deaths) of gameplay rows only
    for parts in rows:
        try:
            t = float(parts[i_t])
            d = int(parts[i_d])
        except ValueError:
            continue
        scene = parts[i_s].strip() if i_s < len(parts) else ""
        if scene in MENU_SCENES:
            continue
        game.append((t, d))
    if len(game) < 2 or game[-1][0] <= game[0][0]:
        return None

    t0, t1 = game[0][0], game[-1][0]
    span = t1 - t0

    events = []
    for i in range(1, len(game)):
        if game[i][1] > game[i - 1][1]:
            events.append((game[i][0] - t0) / span * 100.0)

    curve = [(0.0, game[0][1])]
    for t, d in game:
        curve.append(((t - t0) / span * 100.0, d))

    return {"events": events, "curve": curve, "span": span, "total": game[-1][1]}


def rolling_counts(events, window_pct):
    """Death count inside a trailing window at each grid point of 0-100."""
    grid, counts = [], []
    lo, n = 0, len(events)
    t = 0.0
    while t <= 100.0 + GRID_STEP_PCT:
        while lo < n and events[lo] < t - window_pct:
            lo += 1
        hi, cnt = lo, 0
        while hi < n and events[hi] <= t:
            cnt += 1
            hi += 1
        grid.append(t)
        counts.append(cnt)
        t += GRID_STEP_PCT
    return grid, counts


def legend_style(n):
    """Font size / column count that keeps a right-side legend within the figure."""
    if n <= 12:
        return 9, 1
    if n <= 40:
        return 7, 2
    return 5.5, 2


def main():
    ap = argparse.ArgumentParser(
        description="Plot normalized difficulty curves from Inkform performance-log CSVs.")
    ap.add_argument("folder", nargs="?", default=".",
                    help="folder to scan for *.csv (default: current directory)")
    ap.add_argument("--out", default=None,
                    help="output PNG (default: <folder>/difficulty_curve.png)")
    ap.add_argument("--window", type=float, default=5.0,
                    help="rolling window as percent of session span (default: 5)")
    args = ap.parse_args()

    sessions = []
    for path in sorted(glob.glob(os.path.join(args.folder, "*.csv"))):
        name = os.path.basename(path)
        try:
            s = parse_session(path)
        except Exception as e:  # one malformed file must not kill the batch
            print(f"  skip (parse error): {name}: {e}", file=sys.stderr)
            continue
        if s is None:
            print(f"  skip (not a perf log / no usable data): {name}")
            continue
        s["name"] = os.path.splitext(name)[0]
        sessions.append(s)

    if not sessions:
        print(f"No usable perf-log CSVs found in: {os.path.abspath(args.folder)}")
        return 1

    print(f"\n{'file':<40} {'span':>8} {'deaths':>7} {'deaths/min':>11}")
    for s in sessions:
        rate = s["total"] / (s["span"] / 60.0) if s["span"] > 0 else 0.0
        print(f"{s['name']:<40} {s['span']/60:>6.1f}m {s['total']:>7} {rate:>10.2f}")
    print()

    n = len(sessions)
    fs, ncol = legend_style(n)
    colors = [plt.cm.turbo(k / max(n - 1, 1)) for k in range(n)]

    fig, (ax1, ax2) = plt.subplots(2, 1, figsize=(12, 8), sharex=True,
                                   gridspec_kw={"height_ratios": [3, 2]})
    fig.suptitle("Inkform Difficulty Curve - sessions normalized to equal length (0-100%)",
                 fontsize=14, fontweight="bold")

    for k, s in enumerate(sessions):
        color = colors[k]
        short = s["name"][5:] if s["name"].startswith("perf_") else s["name"]
        label = f"{short}  ({s['span']/60:.0f} min, {s['total']} deaths)"

        grid, counts = rolling_counts(s["events"], args.window)
        minutes = (args.window / 100.0) * s["span"] / 60.0
        rates = [c / minutes if minutes > 0 else 0.0 for c in counts]
        ax1.plot(grid, rates, color=color, lw=1.5, alpha=0.85, label=label)

        if n <= ANNOTATION_LIMIT:
            peak = max(rates)
            if peak > 0:
                pt = grid[rates.index(peak)]
                ax1.annotate(f"peak {peak:.0f}/min @ {pt:.0f}%",
                             xy=(pt, peak),
                             xytext=(pt - 20 if pt > 55 else pt + 3, peak * 1.08),
                             fontsize=8.5, color=color,
                             arrowprops=dict(arrowstyle="->", color=color, lw=0.8))

        xs = [p for p, _ in s["curve"]]
        ys = [d for _, d in s["curve"]]
        ax2.step(xs, ys, where="post", color=color, lw=1.5, alpha=0.85, label=label)

    ax1.set_ylabel(f"Rolling death rate (deaths/min, {args.window:g}% window)")
    ax1.set_xlim(0, 100)
    ax1.set_ylim(bottom=0)
    ax1.grid(alpha=0.25)

    ax2.set_ylabel("Cumulative deaths")
    ax2.set_xlabel("Session progress (%)")
    ax2.set_xlim(0, 100)
    ax2.set_ylim(bottom=0)
    ax2.grid(alpha=0.25)

    # legend outside on the right so it never covers the curves; bbox_inches="tight"
    # grows the saved image to include it
    handles, labels = ax1.get_legend_handles_labels()
    fig.legend(handles, labels, loc="center left", bbox_to_anchor=(1.02, 0.5),
               fontsize=fs, ncol=ncol, frameon=False)

    fig.tight_layout(rect=[0, 0, 1, 0.95])
    out = args.out or os.path.join(args.folder, "difficulty_curve.png")
    fig.savefig(out, dpi=150, bbox_inches="tight")
    print(f"saved: {out}  ({n} sessions, legend right, {ncol} col @ {fs}pt)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
