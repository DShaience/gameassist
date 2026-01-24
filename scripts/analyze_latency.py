#!/usr/bin/env python3
"""
Analyze latency breakdown from GameScreenTranslator interaction logs.

Usage:
    python scripts/analyze_latency.py [path/to/interactions.jsonl]

Default: Finds most recent *_interactions.jsonl in logs/ or GameScreenTranslator/logs/
"""

import json
import sys
from pathlib import Path
from collections import defaultdict
from datetime import datetime

try:
    from tabulate import tabulate
except ImportError:
    print("Please install tabulate: pip install tabulate")
    sys.exit(1)

def percentile(data: list, p: int) -> float:
    """Calculate the p-th percentile of a list."""
    if not data:
        return 0
    sorted_data = sorted(data)
    k = (len(sorted_data) - 1) * p / 100
    f = int(k)
    c = f + 1 if f + 1 < len(sorted_data) else f
    return sorted_data[f] + (sorted_data[c] - sorted_data[f]) * (k - f)

def load_logs(path: Path) -> list[dict]:
    """Load JSONL log file."""
    entries = []
    with open(path, 'r', encoding='utf-8-sig') as f:
        for line in f:
            line = line.strip()
            if line:
                try:
                    entries.append(json.loads(line))
                except json.JSONDecodeError as e:
                    print(f"Warning: Skipping malformed line: {e}")
    return entries

def format_ms(value) -> str:
    """Format milliseconds for display."""
    if value is None:
        return "-"
    return f"{int(value):,}"

def format_pct(value, total) -> str:
    """Format as percentage of total."""
    if value is None or total is None or total == 0:
        return "-"
    pct = value / total * 100
    return f"{pct:.0f}%"

def extract_game_name(window_title: str) -> str:
    """Extract a short game name from window title."""
    if not window_title:
        return "Unknown"
    # Common patterns
    title = window_title.lower()
    if "hades" in title:
        return "Hades"
    if "space quest" in title:
        return "Space Quest IV"
    if "firefox" in title or "chrome" in title or "edge" in title:
        return "Browser"
    # Return first part before common separators
    for sep in [" - ", " — ", " | ", " :: "]:
        if sep in window_title:
            return window_title.split(sep)[0][:20]
    return window_title[:20]

def classify_bottleneck(entry: dict, use_tts_total: bool) -> str:
    """Classify which stage was the bottleneck for this interaction."""
    lat = entry.get("latency_ms", {})
    detailed = lat.get("detailed", {})
    
    stt = detailed.get("stt_ms") or 0
    llm = lat.get("model_call") or 0
    tts_value = (lat.get("tts") if use_tts_total else detailed.get("tts_network_ms")) or 0
    
    tts_label = "TTS_TOT" if use_tts_total else "TTS_NET"
    stages = {"STT": stt, "LLM": llm, tts_label: tts_value}
    return max(stages, key=stages.get) if any(stages.values()) else "Unknown"

def compute_stage_shares(entry: dict) -> dict:
    """Compute stage shares as percentages of total TTFA."""
    lat = entry.get("latency_ms", {})
    detailed = lat.get("detailed", {})
    total = detailed.get("ttfa_user_ms") or lat.get("total") or 0
    
    if total == 0:
        return {}
    
    stt = detailed.get("stt_ms") or 0
    llm = lat.get("model_call") or 0
    tts_net = detailed.get("tts_network_ms") or 0
    tts_total = lat.get("tts") or 0
    
    return {
        "stt_share": stt / total * 100,
        "llm_share": llm / total * 100,
        "tts_network_share": tts_net / total * 100,
        "tts_total_share": tts_total / total * 100,
    }

def main():
    # Determine log file path
    if len(sys.argv) > 1:
        log_path = Path(sys.argv[1])
    else:
        # Try common locations - look for most recent timestamped file
        log_dirs = [
            Path("logs"),
            Path("GameScreenTranslator/logs"),
        ]
        log_path = None
        for log_dir in log_dirs:
            if log_dir.exists():
                # Find most recent .jsonl file (timestamp prefix means sorted = newest last)
                jsonl_files = sorted(log_dir.glob("*_interactions.jsonl"))
                if jsonl_files:
                    log_path = jsonl_files[-1]  # Most recent
                    break
                # Fallback to old-style filename
                old_style = log_dir / "interactions.jsonl"
                if old_style.exists():
                    log_path = old_style
                    break
        if log_path is None:
            print("Error: No log file found. Provide path as argument.")
            print("Usage: python scripts/analyze_latency.py [path/to/*.jsonl]")
            print("       Looks for *_interactions.jsonl in logs/ or GameScreenTranslator/logs/")
            sys.exit(1)

    if not log_path.exists():
        print(f"Error: File not found: {log_path}")
        sys.exit(1)

    print(f"Loading: {log_path}")
    entries = load_logs(log_path)
    print(f"Found {len(entries)} log entries\n")

    # Filter entries with latency data
    timed_entries = [e for e in entries if e.get("latency_ms")]

    if not timed_entries:
        print("No entries with latency data found.")
        return

    # Mark cold start (first entry)
    for i, entry in enumerate(timed_entries):
        entry["_is_cold_start"] = (i == 0)
        entry["_game"] = extract_game_name(entry.get("window_title", ""))
        entry["_bottleneck_net"] = classify_bottleneck(entry, use_tts_total=False)
        entry["_bottleneck_total"] = classify_bottleneck(entry, use_tts_total=True)
        entry["_shares"] = compute_stage_shares(entry)

    # Group by interaction type
    by_type = defaultdict(list)
    for e in timed_entries:
        by_type[e.get("interaction_type", "unknown")].append(e)

    # Group by game
    by_game = defaultdict(list)
    for e in timed_entries:
        by_game[e["_game"]].append(e)

    # === Main Table: All Interactions with Full Breakdown ===
    print("=" * 130)
    print("LATENCY BREAKDOWN BY INTERACTION (all times in ms)")
    print("=" * 130)

    rows = []
    for entry in timed_entries:
        lat = entry.get("latency_ms", {})
        detailed = lat.get("detailed", {})
        total = lat.get("total")
        
        # Get all timing values
        capture = lat.get("capture")
        stt = detailed.get("stt_ms")
        img_scale = detailed.get("image_downscale_ms")
        llm = lat.get("model_call")
        tts_net = detailed.get("tts_network_ms")
        tts_total = lat.get("tts")
        
        row = {
            "Game": entry["_game"][:10],
            "Type": entry.get("interaction_type", "?")[:8],
            "Cold": "Y" if entry["_is_cold_start"] else "",
            "Cap": format_ms(capture),
            "STT": format_ms(stt),
            "Img": format_ms(img_scale),
            "LLM": format_ms(llm),
            "TTS Net": format_ms(tts_net),
            "TTS Tot": format_ms(tts_total),
            "Total": format_ms(total),
            # Percentage breakdown
            "STT%": format_pct(stt, total),
            "LLM%": format_pct(llm, total),
            "TTS Net%": format_pct(tts_net, total),
            "TTS Tot%": format_pct(tts_total, total),
            "Bottle Tot": entry["_bottleneck_total"],
            "Bottle Net": entry["_bottleneck_net"],
        }
        rows.append(row)

    print(tabulate(rows, headers="keys", tablefmt="simple"))
    print()

    # === Bottleneck Analysis ===
    question_entries = [e for e in timed_entries if e.get("interaction_type") == "question_tap"]
    if question_entries:
        print("=" * 80)
        print("BOTTLENECK ANALYSIS (question_tap, TTS total)")
        print("=" * 80)
        
        bottleneck_counts = defaultdict(int)
        for e in question_entries:
            bottleneck_counts[e["_bottleneck_total"]] += 1
        
        total_q = len(question_entries)
        bottleneck_rows = []
        for stage, count in sorted(bottleneck_counts.items(), key=lambda x: -x[1]):
            pct = count / total_q * 100
            bottleneck_rows.append({
                "Stage": stage,
                "Count": count,
                "Rate": f"{pct:.1f}%",
            })
        
        print(tabulate(bottleneck_rows, headers="keys", tablefmt="simple"))
        print()

    # === Summary by Game ===
    print("=" * 100)
    print("SUMMARY BY GAME: P50 / P90 (milliseconds)")
    print("=" * 100)

    game_rows = []
    for game, game_entries in sorted(by_game.items()):
        q_entries = [e for e in game_entries if e.get("interaction_type") == "question_tap"]
        if not q_entries:
            continue
        
        totals = [e["latency_ms"].get("total") or 0 for e in q_entries]
        stts = [e["latency_ms"].get("detailed", {}).get("stt_ms") or 0 for e in q_entries]
        llms = [e["latency_ms"].get("model_call") or 0 for e in q_entries]
        tts_nets = [e["latency_ms"].get("detailed", {}).get("tts_network_ms") or 0 for e in q_entries]
        
        game_rows.append({
            "Game": game,
            "N": len(q_entries),
            "TTFA p50": format_ms(percentile(totals, 50)),
            "TTFA p90": format_ms(percentile(totals, 90)),
            "STT p50": format_ms(percentile(stts, 50)),
            "LLM p50": format_ms(percentile(llms, 50)),
            "TTS Net p50": format_ms(percentile(tts_nets, 50)),
        })

    print(tabulate(game_rows, headers="keys", tablefmt="simple"))
    print()

    # === Cold Start vs Warm ===
    cold_entries = [e for e in question_entries if e["_is_cold_start"]]
    warm_entries = [e for e in question_entries if not e["_is_cold_start"]]
    
    if cold_entries and warm_entries:
        print("=" * 60)
        print("COLD START vs WARM (question_tap only)")
        print("=" * 60)
        
        cold_totals = [e["latency_ms"].get("total") or 0 for e in cold_entries]
        warm_totals = [e["latency_ms"].get("total") or 0 for e in warm_entries]
        
        print(f"  Cold start (N={len(cold_entries)}): p50 = {format_ms(percentile(cold_totals, 50))} ms")
        print(f"  Warm (N={len(warm_entries)}):       p50 = {format_ms(percentile(warm_totals, 50))} ms")
        print()

    # === Token & Character Info ===
    token_rows = []
    for entry in timed_entries:
        lat = entry.get("latency_ms", {})
        detailed = lat.get("detailed", {})
        
        in_tok = detailed.get("llm_input_tokens")
        out_tok = detailed.get("llm_output_tokens")
        tts_chars = detailed.get("tts_char_count")
        llm_ms = lat.get("model_call")
        tts_net = detailed.get("tts_network_ms")
        
        if in_tok or out_tok or tts_chars:
            # Calculate rates
            llm_rate = f"{out_tok / (llm_ms/1000):.0f} tok/s" if out_tok and llm_ms else "-"
            tts_rate = f"{tts_chars / (tts_net/1000):.0f} ch/s" if tts_chars and tts_net else "-"
            
            token_rows.append({
                "Game": entry["_game"][:10],
                "In Tok": format_ms(in_tok),
                "Out Tok": format_ms(out_tok),
                "LLM ms": format_ms(llm_ms),
                "LLM Rate": llm_rate,
                "TTS Ch": format_ms(tts_chars),
                "TTS Net": format_ms(tts_net),
                "TTS Rate": tts_rate,
            })
    
    if token_rows:
        print("=" * 100)
        print("TOKEN & CHARACTER BREAKDOWN")
        print("=" * 100)
        print(tabulate(token_rows, headers="keys", tablefmt="simple"))
        print()

    # === Dev Note Output (copy-paste ready) ===
    print("=" * 80)
    print("DEV NOTE (copy-paste ready)")
    print("=" * 80)
    print()
    print(f"**Log file:** `{log_path.name}`")
    print(f"**Date:** {datetime.now().strftime('%Y-%m-%d')}")
    print(f"**Total interactions:** {len(question_entries)} question_tap")
    print()
    
    if question_entries:
        all_totals = [e["latency_ms"].get("total") or 0 for e in question_entries]
        all_stts = [e["latency_ms"].get("detailed", {}).get("stt_ms") or 0 for e in question_entries]
        all_llms = [e["latency_ms"].get("model_call") or 0 for e in question_entries]
        all_tts_nets = [e["latency_ms"].get("detailed", {}).get("tts_network_ms") or 0 for e in question_entries]
        
        print("**Overall p50/p90:**")
        print(f"- TTFA: {format_ms(percentile(all_totals, 50))} / {format_ms(percentile(all_totals, 90))} ms")
        print(f"- STT: {format_ms(percentile(all_stts, 50))} / {format_ms(percentile(all_stts, 90))} ms")
        print(f"- LLM: {format_ms(percentile(all_llms, 50))} / {format_ms(percentile(all_llms, 90))} ms")
        print(f"- TTS Network: {format_ms(percentile(all_tts_nets, 50))} / {format_ms(percentile(all_tts_nets, 90))} ms")
        print()
        
        print("**Bottleneck rate (TTS total):**")
        for stage, count in sorted(bottleneck_counts.items(), key=lambda x: -x[1]):
            pct = count / len(question_entries) * 100
            print(f"- {stage}: {pct:.0f}%")
        print()
        
        # Slowest traces
        sorted_by_total = sorted(question_entries, key=lambda x: -(x["latency_ms"].get("total") or 0))
        print("**Slowest traces:**")
        for entry in sorted_by_total[:3]:
            lat = entry["latency_ms"]
            detailed = lat.get("detailed", {})
            print(f"- {entry['_game']}: Total={format_ms(lat.get('total'))}ms "
                  f"(STT={format_ms(detailed.get('stt_ms'))}, "
                  f"LLM={format_ms(lat.get('model_call'))}, "
                  f"TTS_Net={format_ms(detailed.get('tts_network_ms'))})")

if __name__ == "__main__":
    main()
