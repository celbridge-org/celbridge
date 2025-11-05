"""Validate and clean a messy Excel sheet using pandas.

What this script does:
1) Reads an Excel sheet where the header row is on Excel row 3 (A:C).
2) Splits a "Month, period" column into separate Month, Start Year, End Year.
3) Converts two numeric text columns (B and C) to millimetres, e.g. "7.5 mm" -> 7.5.
4) Validates that every value in B and C is present, numeric, and non-negative.
5) Writes a clean, formatted Excel file (fixed column widths, mm columns formatted).
"""

import sys
import pandas as pd

# Input / output paths (change these if needed)
INPUT_FILE  = "06_data_cleaning/messy_data.xlsx"
OUTPUT_FILE = "06_data_cleaning/clean_data.xlsx"

# Excel specifics for this sheet
HEADER_ROW_EXCEL = 3       # header line is in row 3 in messy_data.xlsx (1-based indexing)
COLUMNS = "A:C"            # we only read A..C from the input sheet
SHEET_NAME_OUT = "Rainfall"


class ValidationError(Exception):
    """Raised when invalid numeric data is found."""
    pass


def print_fail(msg: str) -> None:
    """Print an error message in red."""
    print(f"\033[31m{msg}\033[0m")


def print_ok(msg: str) -> None:
    """Print a success message in green."""
    print(f"\033[32m{msg}\033[0m")


def clean_text_numbers_to_mm(series: pd.Series) -> pd.Series:
    """Convert text like '3.1mm', '1,234', '7.5 MM', or '  10  ' to numbers."""
    s = series.astype(str)
    s = s.str.replace(r"[,\s]+", "", regex=True)      # remove commas/spaces
    s = s.str.replace(r"[A-Za-z]+", "", regex=True)   # remove letters/units
    s = s.replace({"": pd.NA, "nan": pd.NA})          # blank or 'nan' -> NaN
    return pd.to_numeric(s, errors="coerce")


def validate_numeric_column(raw: pd.Series, parsed: pd.Series, excel_col: str) -> None:
    """Validate a column of numeric data."""
    first_data_row_excel = HEADER_ROW_EXCEL + 1

    for idx in range(len(raw)):
        raw_val = raw.iloc[idx]
        num_val = parsed.iloc[idx]
        excel_row = first_data_row_excel + idx

        if pd.isna(raw_val) or str(raw_val).strip() == "":
            raise ValidationError(f"Invalid number at {excel_col}{excel_row}: blank or empty")

        if pd.isna(num_val):
            raise ValidationError(f"Invalid number at {excel_col}{excel_row}: '{raw_val}'")

        if num_val < 0:
            raise ValidationError(f"Negative value at {excel_col}{excel_row}: {num_val}")


def apply_formatting(ws, df: pd.DataFrame) -> None:
    """Apply cell formatting for output sheet."""
    fixed_width = 18
    mm_cols = {4, 5}  # 1-based column indices: D=4, E=5

    for col_idx in range(1, len(df.columns) + 1):
        if col_idx in mm_cols:
            for row in ws.iter_rows(min_row=2, max_row=ws.max_row, min_col=col_idx, max_col=col_idx):
                row[0].number_format = "0.000"

        letter = ws.cell(row=1, column=col_idx).column_letter
        ws.column_dimensions[letter].width = fixed_width


def process_excel(input_path: str, output_path: str) -> None:
    """Read, validate, clean, and write the Excel data."""
    df = pd.read_excel(
        input_path,
        sheet_name=0,
        header=HEADER_ROW_EXCEL - 1,
        dtype=str,
        usecols=COLUMNS,
    )

    # Remove any completely empty rows, then reset the row numbers
    # (so the DataFrame index runs 0, 1, 2, ... after dropping blanks)
    df = df.dropna(axis=0, how="all").reset_index(drop=True)

    # Split "Month, period" like "January, 2001-2019" into Month, Start Year, End Year
    month_period = df["Month, period"].astype(str)
    parts = month_period.str.split(",", n=1, expand=True)
    month = parts[0].str.strip().str.title()
    period = parts[1].str.strip() if parts.shape[1] > 1 else ""

    # Simple dash-based year splitting (e.g. "2001-2019")
    start_year = []
    end_year = []
    for text in period:
        if isinstance(text, str) and "-" in text:
            left, right = text.split("-", 1)
            start_year.append(pd.to_numeric(left.strip(), errors="coerce"))
            end_year.append(pd.to_numeric(right.strip(), errors="coerce"))
        else:
            start_year.append(pd.NA)
            end_year.append(pd.NA)

    start_year = pd.Series(start_year)
    end_year   = pd.Series(end_year)

    colB_name, colC_name = df.columns[1], df.columns[2]
    colB_raw, colC_raw = df[colB_name], df[colC_name]

    colB_mm = clean_text_numbers_to_mm(colB_raw)
    colC_mm = clean_text_numbers_to_mm(colC_raw)

    validate_numeric_column(colB_raw, colB_mm, "B")
    validate_numeric_column(colC_raw, colC_mm, "C")

    colB_out = f"{colB_name} (mm)" if "(mm)" not in colB_name else colB_name
    colC_out = f"{colC_name} (mm)" if "(mm)" not in colC_name else colC_name

    out = pd.DataFrame({
        "Month": month,
        "Start Year": start_year,
        "End Year": end_year,
        colB_out: colB_mm,
        colC_out: colC_mm,
    })

    with pd.ExcelWriter(output_path, engine="openpyxl") as writer:
        out.to_excel(writer, index=False, sheet_name=SHEET_NAME_OUT)
        ws = writer.sheets[SHEET_NAME_OUT]
        apply_formatting(ws, out)


def process_data() -> None:
    """Entry-point wrapper so beginners can see the flow clearly."""
    try:
        process_excel(INPUT_FILE, OUTPUT_FILE)
        print_ok(f"Cleaned data written to: {OUTPUT_FILE}")
    except ValidationError as e:
        print_fail(str(e))
        sys.exit(1)
    except Exception as e:
        print_fail(f"Unexpected error: {e}")
        sys.exit(1)


if __name__ == "__main__":
    process_data()
