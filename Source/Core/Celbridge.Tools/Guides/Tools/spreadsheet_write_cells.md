---
name: spreadsheet_write_cells
description: Edit shape, formula vs. text resolution, recalculation, and numeric magnitude limits for spreadsheet_write_cells.
---

# spreadsheet_write_cells

Writes a batch of single-cell edits to a worksheet. Other cells in the sheet — including formatting on cells the edits do not touch — are preserved.

## Edit shape

Each edit is an object with:

- `cell` (A1 string, required).
- `value` (number, boolean, string, or null to blank the cell).
- `isFormula` (bool, optional, default false).

## Formula vs. text

Strings that begin with `=` are written as **text** by default. Set `isFormula: true` to write a formula. See `spreadsheet_cell_typing`.

## Recalculation

Formulas are recalculated as part of the save, so a follow-up `spreadsheet_read_sheet` returns fresh computed values.

## Numeric magnitude limit

Numeric values must be finite and must have magnitude at most `1e+300`; values outside that range are rejected because the underlying serialiser rounds them to a string that overflows on reopen.
