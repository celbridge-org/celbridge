# spreadsheet_read_sheet

Reads cell values from a sheet in an .xlsx workbook. By default returns row arrays from the sheet's used range. Cells round-trip with their Excel type preserved (see `spreadsheet_cell_typing`).

## Range

`range` is A1-notation (e.g. `B2:D10`). Empty string reads the sheet's used range. Do not include a sheet qualifier (`Sheet1!A1` is rejected).

## Headers mode

When `headers` is true:

- The first row in the requested range becomes column names.
- Each subsequent row is returned as an object keyed by header.
- Duplicate names get a numeric suffix.
- Empty headers become `column_<letter>`.

## Paging

- `offset` skips that many data rows before returning rows. Use 0 to start at the first data row.
- `limit` is the maximum number of data rows to return. Use 0 to apply the default page size of 1000 rows.

## Column clamping

`columnLimit` is the maximum number of columns to materialise per row. Use 0 to apply the default cap of 256 columns.

The cap protects callers from sheets whose used range has been inflated by a stray write to a far-right column (e.g. `XFD1`) that would otherwise emit a 16384-column row of nulls. Compare to `totalColumnCount` in the response to detect inflation.

## Mode

- `"values"` (default) returns computed cell values.
- `"formulas"` returns the formula text (with leading `=`) for cells that contain a formula.

## totalRowCount semantics

`totalRowCount` is the row count in the read range:

- When `headers` is **false**, this includes any header row.
- When `headers` is **true**, the header row is excluded.
