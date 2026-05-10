# spreadsheet_append_rows

Appends rows to the end of a worksheet's used range in a single open/save cycle. The sheet must already exist; create it first with `spreadsheet_add_sheets` if needed. An empty sheet receives the rows starting at A1.

## rowsJson

A JSON array of rows. Each row is itself an array of cell values starting at column A. Cell values may be numbers, booleans, strings, or `null` to leave a cell blank. Trailing missing values leave those cells blank.

```json
[
  ["Mar", 1200, "shipped"],
  ["Apr", 1450, "pending"]
]
```

## Formulas are written as text

Cell values starting with `=` are written as text, not formulas. Use `spreadsheet_write_cells` with `isFormula: true` for formula writes.

## Returned row numbers

The response reports `firstRow` and `lastRow` as 1-based row numbers in the worksheet, plus `appendedRowCount` (which equals `lastRow - firstRow + 1`). These are the addresses an immediate follow-up read or format call would target.

## Append vs. write_cells

`spreadsheet_append_rows` is the right tool for ingesting tabular data row-by-row at the bottom of a sheet. For scattered single-cell edits, formulas, or edits that overwrite existing cells in place, use `spreadsheet_write_cells`.
