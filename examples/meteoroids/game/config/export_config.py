"""Exports the gameplay settings spreadsheet to the CSV file the game reads.

Edit gameplay.xlsx, then run this script from the Python console:

    %run game/config/export_config.py

and reload the game. Each row of the Settings sheet below the header row becomes
one line of gameplay.csv: the setting's name from column A and its value from
column B. Column C describes the setting for people, so it stays in the
spreadsheet.
"""

import csv
import io

from celbridge import cel

SPREADSHEET = "game/config/gameplay.xlsx"
SHEET = "Settings"
CSV_FILE = "game/config/gameplay.csv"


def read_settings():
    """Returns the sheet's settings as (name, value) pairs, checking each one."""
    # Read columns by position rather than by header, so renaming or clearing a
    # header in the spreadsheet doesn't break the export.
    sheet = cel.spreadsheet.read_sheet(resource=SPREADSHEET, sheet=SHEET)

    settings = []
    # Skip the header row, so the first setting is on row 2.
    for row_number, row in enumerate(sheet["rows"][1:], start=2):
        name, value = (list(row) + [None, None])[:2]

        if name is None and value is None:
            continue  # a blank row, perhaps left between groups of settings
        if not isinstance(name, str) or not name.strip():
            raise ValueError(f"Row {row_number} has a value but no setting name.")
        # Booleans are numbers in Python, so rule them out explicitly.
        if isinstance(value, bool) or not isinstance(value, (int, float)):
            raise ValueError(f"Row {row_number}: {name} should be a number, not {value!r}.")

        # Spreadsheets store every number as a float, so write 270.0 as 270.
        if isinstance(value, float) and value.is_integer():
            value = int(value)
        settings.append((name.strip(), value))

    return settings


def write_csv(settings):
    text = io.StringIO()
    writer = csv.writer(text, lineterminator="\n")
    writer.writerow(["setting", "value"])
    writer.writerows(settings)
    cel.file.write(file_resource=CSV_FILE, content=text.getvalue())


settings = read_settings()
write_csv(settings)
print(f"Wrote {len(settings)} settings to {CSV_FILE}. Reload the game to play with them.")
