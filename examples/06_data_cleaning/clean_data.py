"""Clean a messy Excel sheet using pandas and openpyxl."""

import pandas as pd

INPUT_FILE  = "06_data_cleaning/messy_data.xlsx"
OUTPUT_FILE = "06_data_cleaning/clean_data.xlsx"


def to_mm(series: pd.Series) -> pd.Series:
    """Convert text like '3.1mm', '1,234', or '7.5 MM' to numeric millimetres."""
    s = series.astype(str)
    s = s.str.replace(r"[,\s]+", "", regex=True)      # remove commas and spaces
    s = s.str.replace(r"[A-Za-z]+", "", regex=True)   # strip units like 'mm'
    s = s.replace({"": pd.NA, "nan": pd.NA})
    return pd.to_numeric(s, errors="coerce")


def apply_formatting(ws, df: pd.DataFrame) -> None:
    """
    Apply formatting to the output worksheet:
    - Columns D and E use number format "0.000"
    - All columns use a fixed width
    """
    fixed_width = 18
    mm_col_indices = [4, 5]  # D=4, E=5

    for col_idx, _ in enumerate(df.columns, start=1):
        # Format cols D and E as numeric, skip the header row.
        if col_idx in mm_col_indices:
            for row in ws.iter_rows(
                min_row=2, max_row=ws.max_row,
                min_col=col_idx, max_col=col_idx
            ):
                row[0].number_format = "0.000"

        # Set a fixed width using numeric index
        col_dim = ws.column_dimensions[ws.cell(row=1, column=col_idx).column_letter]
        col_dim.width = fixed_width


def main() -> None:
    # Read the Excel file into a panda's data frame
    # The header is defined in row 3 (which is row index 2 because of 0-based indexing)
    df = pd.read_excel(INPUT_FILE, sheet_name=0, header=2, dtype=str, usecols="A:C")

    # Drop fully-empty rows
    df = df.dropna(axis=0, how="all")

    # Split "Month, period" into Month and Period
    month_period = df["Month, period"].astype(str)
    parts = month_period.str.split(",", n=1, expand=True)
    month_col = parts[0].str.strip().str.title()
    period_col = parts[1].str.strip() if parts.shape[1] > 1 else ""

    # Split time period "2001-2019" into Start Year and End Year
    years = pd.Series(period_col).str.extract(r"(?P<Start>\d{4})\s*-\s*(?P<End>\d{4})")
    start_year = pd.to_numeric(years["Start"], errors="coerce")
    end_year   = pd.to_numeric(years["End"],   errors="coerce")

    # Columns B and C contain the numeric data
    colB_name = df.columns[1]
    colC_name = df.columns[2]

    colB_mm = to_mm(df[colB_name])
    colC_mm = to_mm(df[colC_name])

    # Add mm units to numeric column names
    colB_out = f"{colB_name} (mm)" if "(mm)" not in colB_name else colB_name
    colC_out = f"{colC_name} (mm)" if "(mm)" not in colC_name else colC_name

    # Build the output dataframe
    out = pd.DataFrame({
        "Month": month_col,
        "Start Year": start_year,
        "End Year": end_year,
        colB_out: colB_mm,
        colC_out: colC_mm,
    })

    # Save and apply formatting
    with pd.ExcelWriter(OUTPUT_FILE, engine="openpyxl") as writer:
        sheet = "Rainfall"
        out.to_excel(writer, index=False, sheet_name=sheet)
        ws = writer.sheets[sheet]
        apply_formatting(ws, out)

    print(f"Cleaned data written to: {OUTPUT_FILE}")


if __name__ == "__main__":
    main()
