#!/usr/bin/env python3
"""
Clean 'messy_data.xlsx' into a tidy, analysis-ready Excel file.

Usage:
    python clean_messy_data.py input.xlsx output.xlsx

If input/output paths are omitted, defaults to:
    input:  messy_data.xlsx (in current directory)
    output: messy_data_clean.xlsx (in current directory)
"""

import sys
import re
import unicodedata
import numpy as np
import pandas as pd

def norm_col(s: str) -> str:
    s = unicodedata.normalize("NFKC", str(s)).strip().lower()
    s = re.sub(r"[^0-9a-z]+", "_", s)
    s = re.sub(r"_+", "_", s).strip("_")
    return s

def to_numeric_clean(series: pd.Series) -> pd.Series:
    s = series.astype(str)
    has_percent = s.str.contains("%", na=False).mean() > 0.2
    s = s.str.replace(r"[,\s]", "", regex=True)
    s = s.str.replace(r"[a-zA-Z]+", "", regex=True)  # remove units like 'mm'
    s = s.replace({"": np.nan, "nan": np.nan})
    out = pd.to_numeric(s, errors="coerce")
    if has_percent:
        out = out / 100.0
    return out

def clean_messy_excel(input_path: str, output_path: str) -> None:
    # Read as text to preserve messy formatting
    df = pd.read_excel(input_path, sheet_name=0, dtype=str)
    # Drop fully empty rows/cols
    df = df.dropna(axis=0, how="all")
    df = df.dropna(axis=1, how="all")

    # Try to find a header row containing 'Month, period'
    header_idx = df.index[df.iloc[:,0].astype(str).str.contains("Month, period", na=False)].tolist()
    if header_idx:
        hdr = header_idx[0]
        # Use that row as header
        new_cols = [df.loc[hdr, c] for c in df.columns]
        df.columns = new_cols
        df = df.drop(index=hdr)
    # Normalize column names
    df.columns = [norm_col(c) for c in df.columns]

    # Split month and period
    if "month_period" in df.columns:
        mp = df["month_period"].astype(str).str.split(",", n=1, expand=True)
        if mp.shape[1] == 2:
            df.insert(0, "month", mp[0].str.strip().str.title())
            df.insert(1, "period", mp[1].str.strip())
            df = df.drop(columns=["month_period"])

    # Numeric cleanup for lake_victoria / simiyu if present
    for cand in ["lake_victoria", "simiyu"]:
        if cand in df.columns:
            df[cand + "_mm"] = to_numeric_clean(df[cand])
            df = df.drop(columns=[cand])

    # De-duplicate
    df = df.drop_duplicates().reset_index(drop=True)

    # Sort by month where possible
    month_order = ["Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec"]
    if "month" in df.columns:
        order_map = {m: i for i, m in enumerate(month_order)}
        df["__order"] = df["month"].str[:3].map(order_map)
        if df["__order"].notna().all():
            df = df.sort_values("__order").drop(columns="__order")
        else:
            df = df.drop(columns="__order")

    with pd.ExcelWriter(output_path, engine="openpyxl") as writer:
        df.to_excel(writer, sheet_name="cleaned", index=False)

def main(argv):
    if len(argv) >= 2:
        input_path = argv[1]
    else:
        input_path = "messy_data.xlsx"
    if len(argv) >= 3:
        output_path = argv[2]
    else:
        output_path = "clean_data.xlsx"
    clean_messy_excel(input_path, output_path)
    print(f"Cleaned file written to: {output_path}")

if __name__ == "__main__":
    main(sys.argv)