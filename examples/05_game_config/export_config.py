import pandas as pd
from pathlib import Path

def export(input_xlsx: str | Path, output_csv: str | Path) -> None:
    """
    Convert the first sheet of an .xlsx to .csv.
    Validates numeric values are within [0, 2500].
    If a 'Value' column exists, only that column is validated; otherwise all numeric cells are checked.
    Raises ValueError on validation failure.
    """
    LOW, HIGH = 0, 2500

    df = pd.read_excel(input_xlsx, sheet_name=0)

    # Prefer validating a 'Value' column if present
    value_col = next((c for c in df.columns if str(c).strip().lower() == "value"), None)
    if value_col is not None:
        nums = pd.to_numeric(df[value_col], errors="coerce").dropna()
        bad = nums[(nums < LOW) | (nums > HIGH)]
        if not bad.empty:
            raise ValueError(f"Out-of-range values in '{value_col}': {bad.tolist()}")
    else:
        numeric = df.apply(pd.to_numeric, errors="coerce")
        bad_mask = numeric.notna() & ((numeric < LOW) | (numeric > HIGH))
        if bad_mask.any().any():
            # Summarize a few offending cells
            details = []
            for r_idx, row in bad_mask.iterrows():
                for c_name, is_bad in row.items():
                    if is_bad:
                        details.append(f"Row {r_idx+1}, '{c_name}'={numeric.at[r_idx, c_name]}")
                        if len(details) >= 8:
                            break
                if len(details) >= 8:
                    break
            raise ValueError("Values out of range [0, 2500]:\n  " + "\n  ".join(details))

    Path(output_csv).parent.mkdir(parents=True, exist_ok=True)
    df.to_csv(output_csv, index=False, encoding="utf-8")

# Example:
# export_excel_to_csv("game_config.xlsx", "Config.csv")
